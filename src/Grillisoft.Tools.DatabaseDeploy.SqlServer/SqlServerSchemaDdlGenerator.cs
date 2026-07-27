using System.Data.Common;
using System.Diagnostics;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Database;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer;

/// <summary>
/// Scripts a whole SQL Server database into a single deployable file.
/// </summary>
/// <remarks>
/// The work is split in two: <see cref="SqlServerObjectScripter"/> writes the DDL of one object at
/// a time through SMO, and everything here decides what to script and in which order, reading the
/// catalog views directly. Two rules drive the whole design - the script has to be replayable top
/// to bottom on an empty database, and no single object that cannot be scripted may take the whole
/// run down.
/// </remarks>
internal sealed class SqlServerSchemaDdlGenerator
{
    private const string Terminator = "GO";

    private readonly Func<string, DbCommand> _createCommand;
    private readonly string _connectionString;
    private readonly string _databaseName;
    private readonly string _migrationTable;
    private readonly ILogger _logger;

    private readonly List<(SqlServerObject Object, string Error)> _failures = [];
    private readonly Dictionary<string, SqlServerObject> _byKey = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Schema, string Name, string Type), DbObject> _byName = [];
    private readonly Dictionary<string, int> _scriptedByType = new(StringComparer.OrdinalIgnoreCase);
    private int _statementsWritten;

    public SqlServerSchemaDdlGenerator(
        Func<string, DbCommand> createCommand,
        string connectionString,
        string databaseName,
        string migrationTable,
        ILogger logger)
    {
        _createCommand = createCommand;
        _connectionString = connectionString;
        _databaseName = databaseName;
        _migrationTable = migrationTable;
        _logger = logger;
    }

    public async Task Generate(StreamWriter writer, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("Generating DDL for database {DatabaseName}", _databaseName);

        using var scripter = new SqlServerObjectScripter(_connectionString, _databaseName, _logger);
        scripter.Connect();

        await LogUnsupportedObjectTypes(cancellationToken);
        await LogNonDefaultStorage(cancellationToken);

        var (objects, dependencies) = await Discover(cancellationToken);
        if (objects.Count == 0)
        {
            _logger.LogWarning("No scriptable object found in database {DatabaseName}; the generated script will be empty",
                _databaseName);
            await WriteHeader(writer, 0);
            return;
        }

        _logger.LogInformation("Ordering {ObjectCount} objects using {DependencyCount} dependencies",
            objects.Count, dependencies.Count);

        var graph = new DbObjectsGraph(objects, dependencies, SqlServerObjectType.RankOf, _logger);
        var ordered = graph.GetGraph();

        if (graph.IgnoredDependencies > 0)
        {
            _logger.LogInformation(
                "Ignored {IgnoredCount} dependencies pointing at objects that are not being scripted (run with debug logging to list them)",
                graph.IgnoredDependencies);
        }

        await WriteHeader(writer, ordered.Count);

        _logger.LogInformation("Scripting {ObjectCount} objects", ordered.Count);
        var progress = new ProgressReporter(ordered.Count, _logger);

        foreach (var dbObject in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WriteObject(writer, scripter, dbObject);
            progress.Advance(dbObject);
        }

        await WriteFooter(writer, graph);

        LogSummary(stopwatch.Elapsed, ordered.Count);

        if (_failures.Count > 0)
            throw new SqlServerDdlGenerationException(_databaseName, _failures.Select(f => (f.Object.Key, f.Error)));
    }

    /// <summary>
    /// Objects living in the database that this tool cannot script. They are not an error, but they
    /// are the first thing to check when a deployment of the generated script comes up short.
    /// </summary>
    private async Task LogUnsupportedObjectTypes(CancellationToken cancellationToken)
    {
        var unsupported = await TryQuery(
            SqlServerDdlQueries.UnsupportedObjectTypes,
            "unsupported object types",
            reader => (Type: reader.GetString(0), Count: reader.GetInt32(1)),
            cancellationToken);

        if (unsupported.Count == 0)
            return;

        _logger.LogWarning(
            "Database {DatabaseName} contains {TypeCount} object type(s) that are not scripted: {Types}",
            _databaseName, unsupported.Count,
            string.Join(", ", unsupported.Select(u => $"{u.Type} ({u.Count})")));
    }

    /// <summary>
    /// A filegroup needs physical files that only a DBA can place, so the generated script leaves
    /// every table and index on the default filegroup of the target database. Saying so up front is
    /// the difference between a surprise and a decision.
    /// </summary>
    private async Task LogNonDefaultStorage(CancellationToken cancellationToken)
    {
        var storage = await TryQuery(
            SqlServerDdlQueries.NonDefaultStorage,
            "filegroups and partition schemes in use",
            reader => (Name: reader.GetString(0), Type: reader.GetString(1)),
            cancellationToken);

        if (storage.Count == 0)
            return;

        _logger.LogWarning(
            "Database {DatabaseName} stores objects on {Count} non default filegroup(s) or partition scheme(s): {Storage}. " +
            "The generated script does not carry them and creates everything on the default filegroup of the target database",
            _databaseName, storage.Count, string.Join(", ", storage.Select(s => $"{s.Name} ({s.Type})")));
    }

    private async Task<(List<DbObject> Objects, List<(DbObject, DbObject)> Dependencies)> Discover(
        CancellationToken cancellationToken)
    {
        var excluded = await TryQueryNames(
            SqlServerDdlQueries.MigrationTableObjects, "objects belonging to the migrations table", cancellationToken,
            ("migration_table", _migrationTable));

        excluded.Add(Unqualified(_migrationTable));

        await DiscoverSchemas(cancellationToken);
        await DiscoverObjects(excluded, cancellationToken);
        await DiscoverTypes(cancellationToken);
        await DiscoverXmlSchemaCollections(cancellationToken);
        var dependencies = await DiscoverPartitioning(cancellationToken);

        dependencies.AddRange(await DiscoverIndexes(excluded, cancellationToken));
        dependencies.AddRange(await DiscoverForeignKeys(excluded, cancellationToken));
        dependencies.AddRange(await DiscoverTriggers(excluded, cancellationToken));
        dependencies.AddRange(await GetObjectDependencies(cancellationToken));
        dependencies.AddRange(await GetTypeDependencies(cancellationToken));
        dependencies.AddRange(await GetXmlSchemaCollectionDependencies(cancellationToken));
        dependencies.AddRange(GetSchemaDependencies());

        var objects = _byKey.Values.Select(o => o.DbObject).ToList();
        _logger.LogInformation("Scripting {ObjectCount} objects: {Breakdown}", objects.Count, Breakdown(objects.Select(o => o.Type)));

        return (objects, dependencies);
    }

    private async Task DiscoverSchemas(CancellationToken cancellationToken)
    {
        var schemas = await TryQuery(
            SqlServerDdlQueries.Schemas, "schemas", reader => reader.GetString(0), cancellationToken);

        foreach (var schema in schemas)
            Add(new SqlServerObject(SqlServerObjectType.Find(SqlServerObjectType.Schema)!, string.Empty, schema));

        _logger.LogInformation("Found {SchemaCount} schemas to create", schemas.Count);
    }

    private async Task DiscoverObjects(HashSet<string> excluded, CancellationToken cancellationToken)
    {
        var rows = await Query(
            SqlServerDdlQueries.Objects,
            "database objects",
            reader => (Schema: reader.GetString(0), Name: reader.GetString(1), SysType: reader.GetString(2)),
            cancellationToken);

        _logger.LogInformation("Found {ObjectCount} objects in database {DatabaseName}", rows.Count, _databaseName);

        foreach (var (schema, name, sysType) in rows)
        {
            var type = SqlServerObjectType.FromSysType(sysType);
            if (type is null)
            {
                _logger.LogDebug("Skipping {Schema}.{Name}: sys.objects type {SysType} is not scripted", schema, name, sysType);
                continue;
            }

            if (excluded.Contains(name))
            {
                _logger.LogDebug("Skipping {Schema}.{Name}: belongs to the migrations table", schema, name);
                continue;
            }

            Add(new SqlServerObject(type, schema, name));
        }
    }

    private async Task DiscoverTypes(CancellationToken cancellationToken)
    {
        var rows = await TryQuery(
            SqlServerDdlQueries.Types,
            "user defined types",
            reader => (Schema: reader.GetString(0), Name: reader.GetString(1), IsTableType: reader.GetBoolean(2)),
            cancellationToken);

        foreach (var (schema, name, isTableType) in rows)
        {
            var type = SqlServerObjectType.Find(isTableType ? SqlServerObjectType.TableType : SqlServerObjectType.Type)!;
            Add(new SqlServerObject(type, schema, name));
        }

        if (rows.Count > 0)
            _logger.LogInformation("Found {TypeCount} user defined types", rows.Count);
    }

    private async Task DiscoverXmlSchemaCollections(CancellationToken cancellationToken)
    {
        var rows = await TryQuery(
            SqlServerDdlQueries.XmlSchemaCollections,
            "XML schema collections",
            reader => (Schema: reader.GetString(0), Name: reader.GetString(1)),
            cancellationToken);

        foreach (var (schema, name) in rows)
            Add(new SqlServerObject(SqlServerObjectType.Find(SqlServerObjectType.XmlSchemaCollection)!, schema, name));

        if (rows.Count > 0)
            _logger.LogInformation("Found {CollectionCount} XML schema collections", rows.Count);
    }

    /// <summary>
    /// Partition functions and the schemes built on them. A scheme cannot be created before its
    /// function, which is the one dependency the catalog states outright.
    /// </summary>
    private async Task<List<(DbObject, DbObject)>> DiscoverPartitioning(CancellationToken cancellationToken)
    {
        var functionType = SqlServerObjectType.Find(SqlServerObjectType.PartitionFunction)!;
        var schemeType = SqlServerObjectType.Find(SqlServerObjectType.PartitionScheme)!;

        var functions = await TryQuery(
            SqlServerDdlQueries.PartitionFunctions, "partition functions", reader => reader.GetString(0), cancellationToken);

        foreach (var name in functions)
            Add(new SqlServerObject(functionType, string.Empty, name));

        var schemes = await TryQuery(
            SqlServerDdlQueries.PartitionSchemes,
            "partition schemes",
            reader => (Scheme: reader.GetString(0), Function: reader.GetString(1)),
            cancellationToken);

        var dependencies = new List<(DbObject, DbObject)>();

        foreach (var (scheme, function) in schemes)
        {
            var added = Add(new SqlServerObject(schemeType, string.Empty, scheme));
            Link(dependencies, added, (string.Empty, function, SqlServerObjectType.PartitionFunction));
        }

        if (functions.Count > 0 || schemes.Count > 0)
            _logger.LogInformation("Found {FunctionCount} partition functions and {SchemeCount} partition schemes",
                functions.Count, schemes.Count);

        return dependencies;
    }

    /// <summary>
    /// Indexes are scripted on their own so they land after the table they sit on, which may itself
    /// be scripted late because of a computed column calling a function.
    /// </summary>
    private async Task<List<(DbObject, DbObject)>> DiscoverIndexes(HashSet<string> excluded, CancellationToken cancellationToken)
    {
        var indexType = SqlServerObjectType.Find(SqlServerObjectType.Index)!;

        var rows = await TryQuery(
            SqlServerDdlQueries.Indexes,
            "indexes",
            reader => (Schema: reader.GetString(0), Table: reader.GetString(1), Name: reader.GetString(2), TypeDesc: reader.GetString(3)),
            cancellationToken);

        var dependencies = new List<(DbObject, DbObject)>();
        var byIndex = new Dictionary<(string Schema, string Table, string Name), SqlServerObject>();

        foreach (var (schema, table, name, typeDesc) in rows)
        {
            if (excluded.Contains(table) || excluded.Contains(name))
            {
                _logger.LogDebug("Skipping index {Schema}.{Table}.{Name}: table belongs to the migrations table", schema, table, name);
                continue;
            }

            var index = Add(new SqlServerObject(indexType, schema, name, schema, table));
            byIndex[(schema, table, name)] = index;

            if (!LinkToTableOrView(dependencies, index, schema, table))
                _logger.LogDebug("Index {IndexName} ({TypeDesc}) sits on {Schema}.{Table}, which is not in the object list",
                    name, typeDesc, schema, table);
        }

        var xmlIndexes = await TryQuery(
            SqlServerDdlQueries.XmlIndexDependencies,
            "XML index hierarchies",
            reader => (Schema: reader.GetString(0), Table: reader.GetString(1), Name: reader.GetString(2), Primary: reader.GetString(3)),
            cancellationToken);

        foreach (var (schema, table, name, primary) in xmlIndexes)
        {
            if (byIndex.TryGetValue((schema, table, name), out var secondary) &&
                byIndex.TryGetValue((schema, table, primary), out var primaryIndex))
            {
                dependencies.Add((secondary.DbObject, primaryIndex.DbObject));
                _logger.LogDebug("Secondary XML index {Name} needs the primary XML index {Primary} of {Schema}.{Table}",
                    name, primary, schema, table);
            }
        }

        _logger.LogInformation("Found {IndexCount} indexes to script separately", byIndex.Count);
        return dependencies;
    }

    /// <summary>
    /// Foreign keys are scripted separately and made to depend on both the table that carries them
    /// and the table they point at. That is what pushes them past every <c>CREATE TABLE</c> in the
    /// script, and what makes a cycle between two tables harmless.
    /// </summary>
    private async Task<List<(DbObject, DbObject)>> DiscoverForeignKeys(HashSet<string> excluded, CancellationToken cancellationToken)
    {
        var foreignKeyType = SqlServerObjectType.Find(SqlServerObjectType.ForeignKey)!;

        var rows = await TryQuery(
            SqlServerDdlQueries.ForeignKeys,
            "foreign keys",
            reader => (
                ParentSchema: reader.GetString(0),
                ParentName: reader.GetString(1),
                Name: reader.GetString(2),
                ReferencedSchema: reader.GetString(3),
                ReferencedName: reader.GetString(4)),
            cancellationToken);

        var dependencies = new List<(DbObject, DbObject)>();
        var added = 0;

        foreach (var (parentSchema, parentName, name, referencedSchema, referencedName) in rows)
        {
            if (excluded.Contains(parentName) || excluded.Contains(referencedName))
            {
                _logger.LogDebug("Skipping foreign key {Name}: {Schema}.{Table} belongs to the migrations table",
                    name, parentSchema, parentName);
                continue;
            }

            var foreignKey = Add(new SqlServerObject(foreignKeyType, parentSchema, name, parentSchema, parentName));
            added++;

            Link(dependencies, foreignKey, (parentSchema, parentName, SqlServerObjectType.Table));
            Link(dependencies, foreignKey, (referencedSchema, referencedName, SqlServerObjectType.Table));
        }

        _logger.LogInformation("Found {ForeignKeyCount} foreign keys", added);
        return dependencies;
    }

    private async Task<List<(DbObject, DbObject)>> DiscoverTriggers(HashSet<string> excluded, CancellationToken cancellationToken)
    {
        var triggerType = SqlServerObjectType.Find(SqlServerObjectType.Trigger)!;

        var rows = await TryQuery(
            SqlServerDdlQueries.Triggers,
            "triggers",
            reader => (Schema: reader.GetString(0), Table: reader.GetString(1), Name: reader.GetString(2)),
            cancellationToken);

        var dependencies = new List<(DbObject, DbObject)>();
        var added = 0;

        foreach (var (schema, table, name) in rows)
        {
            if (excluded.Contains(table) || excluded.Contains(name))
            {
                _logger.LogDebug("Skipping trigger {Name}: {Schema}.{Table} belongs to the migrations table", name, schema, table);
                continue;
            }

            var trigger = Add(new SqlServerObject(triggerType, schema, name, schema, table));
            added++;

            LinkToTableOrView(dependencies, trigger, schema, table);
        }

        _logger.LogInformation("Found {TriggerCount} triggers", added);
        return dependencies;
    }

    private async Task<List<(DbObject, DbObject)>> GetObjectDependencies(CancellationToken cancellationToken)
    {
        var rows = await Query(
            SqlServerDdlQueries.ObjectDependencies,
            "object dependencies",
            reader => (
                Schema: reader.GetString(0),
                Name: reader.GetString(1),
                SysType: reader.GetString(2),
                DependsOnSchema: reader.GetString(3),
                DependsOnName: reader.GetString(4),
                DependsOnSysType: reader.GetString(5)),
            cancellationToken);

        var dependencies = new List<(DbObject, DbObject)>();

        foreach (var row in rows)
        {
            var type = SqlServerObjectType.FromSysType(row.SysType);
            var dependsOnType = SqlServerObjectType.FromSysType(row.DependsOnSysType);
            if (type is null || dependsOnType is null)
                continue;

            if (_byName.TryGetValue((row.Schema, row.Name, type.Name), out var dependent) &&
                _byName.TryGetValue((row.DependsOnSchema, row.DependsOnName, dependsOnType.Name), out var dependsOn))
            {
                dependencies.Add((dependent, dependsOn));
            }
        }

        _logger.LogInformation("Found {DependencyCount} dependencies declared by the server", dependencies.Count);
        return dependencies;
    }

    private async Task<List<(DbObject, DbObject)>> GetTypeDependencies(CancellationToken cancellationToken)
    {
        var rows = await TryQuery(
            SqlServerDdlQueries.TypeDependencies,
            "user defined type usages",
            reader => (
                Schema: reader.GetString(0),
                Name: reader.GetString(1),
                SysType: reader.GetString(2),
                TypeSchema: reader.GetString(3),
                TypeName: reader.GetString(4),
                IsTableType: reader.GetBoolean(5)),
            cancellationToken);

        var dependencies = new List<(DbObject, DbObject)>();

        foreach (var row in rows)
        {
            var type = SqlServerObjectType.FromSysType(row.SysType);
            if (type is null || !_byName.TryGetValue((row.Schema, row.Name, type.Name), out var dependent))
                continue;

            var typeName = row.IsTableType ? SqlServerObjectType.TableType : SqlServerObjectType.Type;
            if (_byName.TryGetValue((row.TypeSchema, row.TypeName, typeName), out var dependsOn))
                dependencies.Add((dependent, dependsOn));
        }

        _logger.LogDebug("Found {DependencyCount} dependencies on user defined types", dependencies.Count);
        return dependencies;
    }

    private async Task<List<(DbObject, DbObject)>> GetXmlSchemaCollectionDependencies(CancellationToken cancellationToken)
    {
        var rows = await TryQuery(
            SqlServerDdlQueries.XmlSchemaCollectionDependencies,
            "XML schema collection usages",
            reader => (
                Schema: reader.GetString(0),
                Name: reader.GetString(1),
                SysType: reader.GetString(2),
                CollectionSchema: reader.GetString(3),
                CollectionName: reader.GetString(4)),
            cancellationToken);

        var dependencies = new List<(DbObject, DbObject)>();

        foreach (var row in rows)
        {
            var type = SqlServerObjectType.FromSysType(row.SysType);
            if (type is null || !_byName.TryGetValue((row.Schema, row.Name, type.Name), out var dependent))
                continue;

            if (_byName.TryGetValue((row.CollectionSchema, row.CollectionName, SqlServerObjectType.XmlSchemaCollection), out var dependsOn))
                dependencies.Add((dependent, dependsOn));
        }

        _logger.LogDebug("Found {DependencyCount} dependencies on XML schema collections", dependencies.Count);
        return dependencies;
    }

    /// <summary>Nothing can be created in a schema that does not exist yet.</summary>
    private List<(DbObject, DbObject)> GetSchemaDependencies()
    {
        var dependencies = new List<(DbObject, DbObject)>();

        foreach (var dbObject in _byKey.Values.Where(o => !string.IsNullOrEmpty(o.Schema)))
        {
            if (_byName.TryGetValue((string.Empty, dbObject.Schema, SqlServerObjectType.Schema), out var schema))
                dependencies.Add((dbObject.DbObject, schema));
        }

        _logger.LogDebug("Found {DependencyCount} objects living in a schema that has to be created first", dependencies.Count);
        return dependencies;
    }

    /// <summary>
    /// Registers an object to be scripted. Duplicates are tolerated: an index name is only unique
    /// within its table and the catalog can hand back the same object twice through two views.
    /// </summary>
    private SqlServerObject Add(SqlServerObject dbObject)
    {
        if (!_byKey.TryAdd(dbObject.Key, dbObject))
            return _byKey[dbObject.Key];

        _byName[(dbObject.Schema, dbObject.Name, dbObject.Type.Name)] = dbObject.DbObject;
        return dbObject;
    }

    private void Link(
        List<(DbObject, DbObject)> dependencies,
        SqlServerObject dependent,
        (string Schema, string Name, string Type) dependsOn)
    {
        if (_byName.TryGetValue(dependsOn, out var target))
            dependencies.Add((dependent.DbObject, target));
    }

    /// <summary>An index or a trigger can hang off a table or off an indexed view.</summary>
    private bool LinkToTableOrView(
        List<(DbObject, DbObject)> dependencies,
        SqlServerObject dependent,
        string schema,
        string name)
    {
        foreach (var type in new[] { SqlServerObjectType.Table, SqlServerObjectType.View })
        {
            if (_byName.TryGetValue((schema, name, type), out var target))
            {
                dependencies.Add((dependent.DbObject, target));
                return true;
            }
        }

        return false;
    }

    private async Task WriteObject(StreamWriter writer, SqlServerObjectScripter scripter, DbObject dbObject)
    {
        if (!_byKey.TryGetValue(dbObject.Key, out var target))
        {
            _logger.LogWarning("Skipping {ObjectKey}: it is not one of the discovered objects", dbObject.Key);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        IReadOnlyList<string> batches;

        try
        {
            batches = scripter.Script(target);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var error = Describe(ex);
            _logger.LogError(ex, "Failed to script {ObjectKey}: {Error}", dbObject.Key, error);
            _failures.Add((target, error));
            await WriteComment(writer, $"!! FAILED to script {target.Type.Name} {target.QualifiedName}: {error}");
            return;
        }

        if (batches.Count == 0)
        {
            _logger.LogWarning("Skipping {ObjectKey}: SMO returned no statement", dbObject.Key);
            return;
        }

        foreach (var batch in batches)
            await WriteStatement(writer, batch);

        _scriptedByType[dbObject.Type] = _scriptedByType.GetValueOrDefault(dbObject.Type) + 1;

        _logger.LogDebug("Scripted {ObjectKey} into {BatchCount} batch(es) in {ElapsedMs}ms",
            dbObject.Key, batches.Count, stopwatch.ElapsedMilliseconds);
    }

    private async Task WriteHeader(StreamWriter writer, int objectCount)
    {
        await WriteComment(writer, new string('=', 78));
        await WriteComment(writer, $"Database {_databaseName} - {objectCount} object(s)");
        await WriteComment(writer, $"Generated by dbdeploy on {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        await WriteComment(writer, "Do not edit: regenerate instead");
        await WriteComment(writer, new string('=', 78));
        await writer.WriteLineAsync();
    }

    private async Task WriteFooter(StreamWriter writer, DbObjectsGraph graph)
    {
        if (graph.BrokenCycles.Count == 0 && _failures.Count == 0)
            return;

        await writer.WriteLineAsync();
        await WriteComment(writer, new string('=', 78));

        foreach (var cycle in graph.BrokenCycles)
        {
            await WriteComment(writer,
                $"Dependency cycle, objects may be created invalid: {string.Join(" -> ", cycle.Select(o => o.Key))}");
        }

        foreach (var (dbObject, error) in _failures)
            await WriteComment(writer, $"!! {dbObject.Key} could not be scripted: {error}");

        await WriteComment(writer, new string('=', 78));
    }

    private static async Task WriteComment(StreamWriter writer, string comment)
    {
        foreach (var line in comment.Split('\n'))
            await writer.WriteLineAsync("-- " + line.TrimEnd('\r'));
    }

    private async Task WriteStatement(StreamWriter writer, string statement)
    {
        await writer.WriteLineAsync(statement.Trim());
        await writer.WriteLineAsync(Terminator);
        await writer.WriteLineAsync();
        _statementsWritten++;
    }

    private void LogSummary(TimeSpan elapsed, int objectCount)
    {
        _logger.LogInformation(
            "Scripted {ScriptedCount}/{ObjectCount} objects into {StatementCount} batches in {Elapsed}: {Breakdown}",
            _scriptedByType.Values.Sum(), objectCount, _statementsWritten, elapsed,
            string.Join(", ", _scriptedByType.OrderBy(kv => SqlServerObjectType.RankOf(kv.Key)).Select(kv => $"{kv.Key} ({kv.Value})")));

        if (_failures.Count > 0)
        {
            _logger.LogError("{FailureCount} object(s) could not be scripted: {Objects}",
                _failures.Count, string.Join(", ", _failures.Select(f => f.Object.Key)));
        }
    }

    private async Task<List<T>> Query<T>(
        string sql,
        string description,
        Func<DbDataReader, T> read,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        _logger.LogDebug("Reading {Description} from database {DatabaseName}", description, _databaseName);

        await using var command = _createCommand(sql);
        foreach (var (name, value) in parameters)
            command.AddParameter(name, value);

        var results = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(read(reader));

        return results;
    }

    /// <summary>
    /// Same as <see cref="Query{T}"/> but a failure is only a warning. Used for the catalog views a
    /// restricted login may not be allowed to read, where losing the result degrades the script
    /// rather than making it impossible.
    /// </summary>
    private async Task<List<T>> TryQuery<T>(
        string sql,
        string description,
        Func<DbDataReader, T> read,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        try
        {
            return await Query(sql, description, read, cancellationToken, parameters);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not read {Description} from database {DatabaseName}; carrying on without it",
                description, _databaseName);
            return [];
        }
    }

    private async Task<HashSet<string>> TryQueryNames(
        string sql,
        string description,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        var names = await TryQuery(sql, description, reader => reader.GetString(0), cancellationToken, parameters);
        return names.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The migrations table may be configured schema qualified; the catalog is not.</summary>
    private static string Unqualified(string name)
    {
        var dot = name.LastIndexOf('.');
        return (dot < 0 ? name : name[(dot + 1)..]).Trim('[', ']');
    }

    private static string Breakdown(IEnumerable<string> types)
        => string.Join(", ", types.GroupBy(t => t)
                                  .OrderBy(g => SqlServerObjectType.RankOf(g.Key))
                                  .Select(g => $"{g.Key} ({g.Count()})"));

    /// <summary>
    /// SMO buries the reason inside a chain of wrapper exceptions; the innermost one is the only
    /// message worth putting in front of a user.
    /// </summary>
    private static string Describe(Exception ex)
    {
        var innermost = ex;
        while (innermost.InnerException is { } inner)
            innermost = inner;

        return innermost is SqlException sql
            ? $"Msg {sql.Number}: {sql.Message.Trim()}"
            : innermost.Message.Trim();
    }

    /// <summary>
    /// Periodic progress on a long run, so a database with thousands of objects does not look hung.
    /// </summary>
    private sealed class ProgressReporter(int total, ILogger logger)
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly int _interval = Math.Max(25, total / 20);
        private int _done;

        public void Advance(DbObject dbObject)
        {
            _done++;

            if (_done % _interval != 0 && _done != total)
                return;

            logger.LogInformation("Scripted {Done}/{Total} objects ({Percent}%) in {Elapsed}, last was {ObjectKey}",
                _done, total, _done * 100 / total, _stopwatch.Elapsed, dbObject.Key);
        }
    }
}
