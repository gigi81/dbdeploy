using System.Data.Common;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Database;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer;

/// <summary>
/// Works out what has to be scripted, and what has to exist before what, by reading the catalog
/// views.
/// </summary>
/// <remarks>
/// SMO knows how to write the DDL of a single object but nothing about the order the objects have
/// to be written in, and asking it to work the order out is an all or nothing operation that a
/// single unscriptable object takes down. So the ordering is dbdeploy's own, and this is where it
/// comes from: every object that can be scripted, plus every pair of objects where the first can
/// only be created once the second exists. Turning that into an order is
/// <see cref="DbObjectsGraph"/>'s job, and writing it out is
/// <see cref="SqlServerSchemaDdlGenerator"/>'s.
/// </remarks>
internal sealed class SqlServerObjectsDiscovery
{
    private readonly CatalogReader _catalog;
    private readonly string _databaseName;
    private readonly string _migrationTable;
    private readonly ILogger _logger;

    /// <summary>Every object to be scripted, by <see cref="DbObject.Key"/>.</summary>
    private readonly Dictionary<string, SqlServerObject> _byKey = new(StringComparer.Ordinal);

    /// <summary>The same objects by the coordinates the catalog names them with.</summary>
    private readonly Dictionary<(string Schema, string Name, string Type), DbObject> _byName = [];

    public SqlServerObjectsDiscovery(
        CatalogReader catalog,
        string databaseName,
        string migrationTable,
        ILogger logger)
    {
        _catalog = catalog;
        _databaseName = databaseName;
        _migrationTable = migrationTable;
        _logger = logger;
    }

    /// <summary>The object behind a graph node, or null when it was never discovered.</summary>
    public SqlServerObject? Find(DbObject dbObject) => _byKey.GetValueOrDefault(dbObject.Key);

    public async Task<(List<DbObject> Objects, List<(DbObject, DbObject)> Dependencies)> Discover(
        CancellationToken cancellationToken)
    {
        await LogUnsupportedObjectTypes(cancellationToken);
        await LogNonDefaultStorage(cancellationToken);

        var excluded = await TryQueryNames(
            SqlServerDdlQueries.MigrationTableObjects, "objects belonging to the migrations table", cancellationToken,
            ("migration_table", _migrationTable));

        excluded.Add(_migrationTable.Unqualified());

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

        _logger.LogInformation("Scripting {ObjectCount} objects: {Breakdown}",
            objects.Count, objects.Select(o => o.Type).Breakdown(SqlServerObjectType.RankOf));

        return (objects, dependencies);
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

    private Task<List<T>> Query<T>(
        string sql,
        string description,
        Func<DbDataReader, T> read,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
        => _catalog.Query(sql, description, read, cancellationToken, parameters);

    private Task<List<T>> TryQuery<T>(
        string sql,
        string description,
        Func<DbDataReader, T> read,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
        => _catalog.TryQuery(sql, description, read, cancellationToken, parameters);

    /// <summary>SQL Server identifiers are compared without regard to case by default.</summary>
    private Task<HashSet<string>> TryQueryNames(
        string sql,
        string description,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
        => _catalog.TryQueryNames(sql, description, StringComparer.OrdinalIgnoreCase, cancellationToken, parameters);
}
