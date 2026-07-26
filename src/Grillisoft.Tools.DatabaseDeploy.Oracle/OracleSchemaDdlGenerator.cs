using System.Data.Common;
using System.Diagnostics;
using System.Text;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Database;
using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle;

/// <summary>
/// Scripts a whole Oracle schema into a single deployable file.
/// </summary>
/// <remarks>
/// Let <c>DBMS_METADATA</c> produce the DDL, after configuring the session transform so the output is
/// portable, and never let a single object that cannot be scripted take the whole run down.
/// Ordering is dbdeploy's own problem, since the script has to be replayable top to bottom on an
/// empty schema, and is solved with <see cref="OracleObjectsGraph"/>.
/// </remarks>
internal sealed class OracleSchemaDdlGenerator
{
    private const string Terminator = "/";

    private readonly Func<string, DbCommand> _createCommand;
    private readonly string _schema;
    private readonly string _migrationTable;
    private readonly ILogger _logger;

    private readonly List<(DbObject Object, string Error)> _failures = [];
    private readonly Dictionary<string, int> _scriptedByType = new(StringComparer.OrdinalIgnoreCase);
    private int _statementsWritten;
    private int _fallbacksUsed;

    public OracleSchemaDdlGenerator(
        Func<string, DbCommand> createCommand,
        string schema,
        string migrationTable,
        ILogger logger)
    {
        _createCommand = createCommand;
        _schema = schema;
        _migrationTable = migrationTable;
        _logger = logger;
    }

    public async Task Generate(StreamWriter writer, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("Generating DDL for schema {SchemaName}", _schema);

        await ApplyTransformParameters(cancellationToken);
        await LogUnsupportedObjectTypes(cancellationToken);

        var (objects, dependencies) = await Discover(cancellationToken);
        if (objects.Count == 0)
        {
            _logger.LogWarning("No scriptable object found in schema {SchemaName}; the generated script will be empty", _schema);
            await WriteHeader(writer, objects.Count);
            return;
        }

        _logger.LogInformation("Ordering {ObjectCount} objects using {DependencyCount} dependencies", objects.Count, dependencies.Count);
        var graph = new OracleObjectsGraph(objects, dependencies, _logger);
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
            await WriteObject(writer, dbObject, cancellationToken);
            progress.Advance(dbObject);
        }

        await WriteComments(writer, ordered, cancellationToken);
        await WriteFooter(writer, graph);

        LogSummary(stopwatch.Elapsed, ordered.Count);

        if (_failures.Count > 0)
            throw new OracleDdlGenerationException(_schema, _failures.Select(f => (f.Object.Key, f.Error)));
    }

    /// <summary>
    /// Configures the session so <c>DBMS_METADATA</c> emits DDL that can be replayed on another
    /// database. Applied once per generation; the previous implementation re-ran it before every
    /// single object.
    /// </summary>
    private async Task ApplyTransformParameters(CancellationToken cancellationToken)
    {
        var block = BuildTransformBlock(OracleDdlQueries.TransformParameters);

        try
        {
            await using var command = _createCommand(block);
            await command.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogDebug("Applied DDL transform parameters: {Parameters}",
                string.Join(", ", OracleDdlQueries.TransformParameters.Select(p => $"{p.Name}={p.Value}")));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not apply the DDL transform parameters in a single block; applying them one by one");
            await ApplyTransformParametersIndividually(cancellationToken);
        }
    }

    /// <summary>
    /// Older releases reject transform parameters that newer ones accept. Applying them one at a
    /// time keeps the ones that work and names the ones that do not.
    /// </summary>
    private async Task ApplyTransformParametersIndividually(CancellationToken cancellationToken)
    {
        foreach (var parameter in OracleDdlQueries.TransformParameters)
        {
            try
            {
                await using var command = _createCommand(BuildTransformBlock([parameter]));
                await command.ExecuteNonQueryAsync(cancellationToken);
                _logger.LogDebug("Applied DDL transform parameter {Parameter}={Value}", parameter.Name, parameter.Value);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "DDL transform parameter {Parameter}={Value} was rejected by the server ({Error}); the generated DDL may be less portable",
                    parameter.Name, parameter.Value, Describe(ex));
            }
        }
    }

    private static string BuildTransformBlock(IEnumerable<(string Name, bool Value)> parameters)
    {
        var block = new StringBuilder("BEGIN").AppendLine();

        foreach (var (name, value) in parameters)
        {
            block.Append("  DBMS_METADATA.SET_TRANSFORM_PARAM(DBMS_METADATA.SESSION_TRANSFORM, '")
                 .Append(name)
                 .Append("', ")
                 .Append(value ? "TRUE" : "FALSE")
                 .AppendLine(");");
        }

        return block.Append("END;").ToString();
    }

    /// <summary>
    /// Objects living in the schema that this tool cannot script. They are not an error, but they
    /// are the first thing to check when a deployment of the generated script comes up short.
    /// </summary>
    private async Task LogUnsupportedObjectTypes(CancellationToken cancellationToken)
    {
        var unsupported = await TryQuery(
            OracleDdlQueries.UnsupportedObjectTypes,
            "unsupported object types",
            reader => (Type: reader.GetString(0), Count: Convert.ToInt32(reader.GetValue(1))),
            cancellationToken,
            ("owner", _schema));

        if (unsupported.Count == 0)
            return;

        _logger.LogWarning(
            "Schema {SchemaName} contains {TypeCount} object type(s) that are not scripted: {Types}",
            _schema, unsupported.Count,
            string.Join(", ", unsupported.Select(u => $"{u.Type} ({u.Count})")));
    }

    private async Task<(List<DbObject> Objects, List<OracleObjectDependencies> Dependencies)> Discover(
        CancellationToken cancellationToken)
    {
        var excludedTables = await GetExcludedTables(cancellationToken);
        var excludedNames = await TryQueryNames(
            OracleDdlQueries.MigrationTableObjects, "objects belonging to the migrations table", cancellationToken,
            ("owner", _schema), ("migration_table", _migrationTable));

        excludedNames.Add(_migrationTable);

        var objects = await Query(
            OracleDdlQueries.Objects,
            "schema objects",
            reader => new DbObject(reader.GetString(0), reader.GetString(1)),
            cancellationToken,
            ("owner", _schema));

        _logger.LogInformation("Found {ObjectCount} objects in schema {SchemaName}: {Breakdown}",
            objects.Count, _schema, Breakdown(objects.Select(o => o.Type)));

        var kept = new List<DbObject>(objects.Count);
        foreach (var dbObject in objects.Distinct())
        {
            if (excludedNames.Contains(dbObject.Name))
            {
                _logger.LogDebug("Skipping {ObjectKey}: belongs to the migrations table", dbObject.Key);
                continue;
            }

            if (dbObject.Type == "TABLE" && excludedTables.Contains(dbObject.Name))
            {
                _logger.LogDebug("Skipping {ObjectKey}: storage table managed by another object", dbObject.Key);
                continue;
            }

            kept.Add(dbObject);
        }

        var dependencies = await GetDependencies(cancellationToken);
        var droppedIndexes = await GetIndexDependencies(kept, excludedTables, excludedNames, dependencies, cancellationToken);
        await AddForeignKeys(kept, excludedNames, dependencies, cancellationToken);
        await AddTriggerDependencies(kept, dependencies, cancellationToken);
        AddBodyDependencies(kept, dependencies);

        var final = kept.Where(o => o.Type != "INDEX" || !droppedIndexes.Contains(o.Name)).ToList();

        _logger.LogInformation("Scripting {ObjectCount} objects: {Breakdown}", final.Count, Breakdown(final.Select(o => o.Type)));

        return (final, dependencies);
    }

    /// <summary>
    /// Tables that exist only to store something else: nested table columns, materialized view
    /// containers, index organized table overflow segments and Advanced Queuing tables. Scripting
    /// them produces a <c>CREATE TABLE</c> that either fails or shadows the real object.
    /// </summary>
    private async Task<HashSet<string>> GetExcludedTables(CancellationToken cancellationToken)
    {
        var excluded = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (sql, description) in new[]
                 {
                     (OracleDdlQueries.NestedTables, "nested table storage tables"),
                     (OracleDdlQueries.MaterializedViewTables, "materialized view container tables"),
                     (OracleDdlQueries.IotSegments, "index organized table segments"),
                     (OracleDdlQueries.QueueTables, "advanced queuing tables"),
                 })
        {
            var names = await TryQueryNames(sql, description, cancellationToken, ("owner", _schema));
            if (names.Count > 0)
                _logger.LogDebug("Excluding {Count} {Description}", names.Count, description);

            excluded.UnionWith(names);
        }

        return excluded;
    }

    private async Task<List<OracleObjectDependencies>> GetDependencies(CancellationToken cancellationToken)
    {
        var dependencies = await Query(
            OracleDdlQueries.Dependencies,
            "object dependencies",
            reader => new OracleObjectDependencies(
                new DbObject(reader.GetString(0), reader.GetString(1)),
                new DbObject(reader.GetString(2), reader.GetString(3))),
            cancellationToken,
            ("owner", _schema));

        _logger.LogInformation("Found {DependencyCount} dependencies declared by the server", dependencies.Count);
        return dependencies;
    }

    /// <summary>
    /// <c>ALL_DEPENDENCIES</c> knows nothing about indexes, so the link to the indexed table is
    /// added here. Returns the indexes that must not be scripted: the ones backing a primary or
    /// unique key, LOB indexes and anything sitting on a table that was already excluded.
    /// </summary>
    /// <remarks>
    /// Reporting what to drop rather than what to keep matters: if the dictionary read fails, the
    /// script ends up with an index too many, not with every index missing.
    /// </remarks>
    private async Task<HashSet<string>> GetIndexDependencies(
        List<DbObject> objects,
        HashSet<string> excludedTables,
        HashSet<string> excludedNames,
        List<OracleObjectDependencies> dependencies,
        CancellationToken cancellationToken)
    {
        var constraintIndexes = await TryQueryNames(
            OracleDdlQueries.ConstraintIndexes, "constraint indexes", cancellationToken, ("owner", _schema));

        var indexes = await TryQuery(
            OracleDdlQueries.Indexes,
            "indexes",
            reader => (Index: reader.GetString(0), Table: reader.GetString(1), Type: reader.GetString(2)),
            cancellationToken,
            ("owner", _schema));

        var tables = ByName(objects, "TABLE", "MATERIALIZED VIEW");
        var dropped = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (index, table, type) in indexes)
        {
            if (constraintIndexes.Contains(index))
            {
                _logger.LogDebug("Skipping index {IndexName}: created by a primary or unique key of {TableName}", index, table);
                dropped.Add(index);
                continue;
            }

            if (type.StartsWith("LOB", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Skipping index {IndexName}: LOB index of {TableName}", index, table);
                dropped.Add(index);
                continue;
            }

            if (excludedNames.Contains(index) || excludedTables.Contains(table) || excludedNames.Contains(table))
            {
                _logger.LogDebug("Skipping index {IndexName}: table {TableName} is not being scripted", index, table);
                dropped.Add(index);
                continue;
            }

            if (tables.TryGetValue(table, out var target))
                dependencies.Add(new OracleObjectDependencies(new DbObject(index, "INDEX"), target));
            else
                _logger.LogDebug("Index {IndexName} sits on {TableName}, which is not in the object list", index, table);
        }

        // Constraint indexes are dropped even when the index read failed, because scripting one on
        // top of a table that already declares its primary key fails with ORA-00955.
        dropped.UnionWith(constraintIndexes);

        _logger.LogDebug("Excluding {Count} indexes that come out with their table", dropped.Count);
        return dropped;
    }

    /// <summary>
    /// Foreign keys are not schema objects, so they are synthesized here and made to depend on both
    /// the table that carries them and the table they point at. That is what pushes them past every
    /// <c>CREATE TABLE</c> in the script.
    /// </summary>
    private async Task AddForeignKeys(
        List<DbObject> objects,
        HashSet<string> excludedNames,
        List<OracleObjectDependencies> dependencies,
        CancellationToken cancellationToken)
    {
        var foreignKeys = await TryQuery(
            OracleDdlQueries.ForeignKeys,
            "foreign keys",
            reader => (Name: reader.GetString(0), Table: reader.GetString(1), Referenced: reader.GetString(2)),
            cancellationToken,
            ("owner", _schema));

        var byName = ByName(objects, "TABLE");
        var added = 0;

        foreach (var (name, table, referenced) in foreignKeys)
        {
            if (excludedNames.Contains(name) || excludedNames.Contains(table) || !byName.ContainsKey(table))
            {
                _logger.LogDebug("Skipping foreign key {ConstraintName}: table {TableName} is not being scripted", name, table);
                continue;
            }

            var constraint = new DbObject(name, OracleObjectType.RefConstraint);
            objects.Add(constraint);
            added++;

            foreach (var dependency in new[] { table, referenced })
            {
                if (byName.TryGetValue(dependency, out var target))
                    dependencies.Add(new OracleObjectDependencies(constraint, target));
            }
        }

        _logger.LogInformation("Found {ForeignKeyCount} foreign keys", added);
    }

    /// <summary>
    /// <c>ALL_DEPENDENCIES</c> usually records the trigger to table link, but not for every trigger
    /// kind, so it is added explicitly.
    /// </summary>
    private async Task AddTriggerDependencies(
        List<DbObject> objects,
        List<OracleObjectDependencies> dependencies,
        CancellationToken cancellationToken)
    {
        var triggers = await TryQuery(
            OracleDdlQueries.Triggers,
            "triggers",
            reader => (Trigger: reader.GetString(0), Table: reader.GetString(1), BaseType: reader.GetString(2)),
            cancellationToken,
            ("owner", _schema));

        var byKey = ByKey(objects);

        foreach (var (trigger, table, baseType) in triggers)
        {
            if (!byKey.TryGetValue((trigger, "TRIGGER"), out var triggerObject))
                continue;

            if (byKey.TryGetValue((table, baseType), out var target))
                dependencies.Add(new OracleObjectDependencies(triggerObject, target));
        }
    }

    /// <summary>
    /// A package or type body cannot compile before its specification. The server does record this,
    /// but the pair is important enough to not rely on it.
    /// </summary>
    private static void AddBodyDependencies(List<DbObject> objects, List<OracleObjectDependencies> dependencies)
    {
        var byKey = ByKey(objects);

        foreach (var (bodyType, specType) in new[] { ("PACKAGE BODY", "PACKAGE"), ("TYPE BODY", "TYPE") })
        {
            foreach (var body in objects.Where(o => o.Type == bodyType).ToList())
            {
                if (byKey.TryGetValue((body.Name, specType), out var spec))
                    dependencies.Add(new OracleObjectDependencies(body, spec));
            }
        }
    }

    /// <summary>Objects of the given types indexed by name, tolerating duplicates.</summary>
    private static Dictionary<string, DbObject> ByName(IEnumerable<DbObject> objects, params string[] types)
    {
        var result = new Dictionary<string, DbObject>(StringComparer.Ordinal);

        foreach (var dbObject in objects.Where(o => types.Contains(o.Type, StringComparer.Ordinal)))
            result.TryAdd(dbObject.Name, dbObject);

        return result;
    }

    /// <summary>All objects indexed by name and type, tolerating duplicates.</summary>
    private static Dictionary<(string Name, string Type), DbObject> ByKey(IEnumerable<DbObject> objects)
    {
        var result = new Dictionary<(string, string), DbObject>();

        foreach (var dbObject in objects)
            result.TryAdd((dbObject.Name, dbObject.Type), dbObject);

        return result;
    }

    private async Task WriteObject(StreamWriter writer, DbObject dbObject, CancellationToken cancellationToken)
    {
        var type = OracleObjectType.Find(dbObject.Type);
        if (type is null)
        {
            _logger.LogWarning("Skipping {ObjectKey}: unsupported object type", dbObject.Key);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        string? ddl;

        try
        {
            ddl = await GetDdl(dbObject, type, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var error = Describe(ex);
            _logger.LogError(ex, "Failed to script {ObjectKey}: {Error}", dbObject.Key, error);
            _failures.Add((dbObject, error));
            await WriteComment(writer, $"!! FAILED to script {dbObject.Type} {dbObject.Name}: {error}");
            return;
        }

        if (string.IsNullOrWhiteSpace(ddl))
        {
            _logger.LogWarning("Skipping {ObjectKey}: the server returned no DDL", dbObject.Key);
            return;
        }

        var statements = OracleDdlSplitter.Split(ddl, type.IsPlSql);
        if (statements.Count == 0)
        {
            _logger.LogWarning("Skipping {ObjectKey}: the DDL returned by the server holds no statement", dbObject.Key);
            return;
        }

        foreach (var statement in statements)
            await WriteStatement(writer, statement);

        _scriptedByType[dbObject.Type] = _scriptedByType.GetValueOrDefault(dbObject.Type) + 1;

        _logger.LogDebug("Scripted {ObjectKey} into {StatementCount} statement(s), {Length} chars, in {ElapsedMs}ms",
            dbObject.Key, statements.Count, ddl.Length, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>
    /// Asks <c>DBMS_METADATA</c> for the DDL, retrying with the alternate object type name when the
    /// server does not know the one we tried, and falling back to the dictionary source when it
    /// refuses altogether.
    /// </summary>
    private async Task<string?> GetDdl(DbObject dbObject, OracleObjectType type, CancellationToken cancellationToken)
    {
        try
        {
            return await GetMetadataDdl(dbObject.Name, type.MetadataType, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "DBMS_METADATA.GET_DDL('{MetadataType}', '{ObjectName}') failed", type.MetadataType, dbObject.Name);

            if (type.FallbackMetadataType is { } alternate)
            {
                try
                {
                    var ddl = await GetMetadataDdl(dbObject.Name, alternate, cancellationToken);
                    _logger.LogDebug("Scripted {ObjectKey} using the alternate metadata type {MetadataType}", dbObject.Key, alternate);
                    return ddl;
                }
                catch (Exception alternateEx) when (alternateEx is not OperationCanceledException)
                {
                    _logger.LogDebug(alternateEx, "DBMS_METADATA.GET_DDL('{MetadataType}', '{ObjectName}') failed", alternate, dbObject.Name);
                }
            }

            var source = await GetSourceDdl(dbObject, type, cancellationToken);
            if (source is null)
                throw;

            _fallbacksUsed++;
            _logger.LogWarning(
                "DBMS_METADATA could not script {ObjectKey} ({Error}); rebuilt the statement from the data dictionary instead",
                dbObject.Key, Describe(ex));

            return source;
        }
    }

    private async Task<string?> GetMetadataDdl(string objectName, string metadataType, CancellationToken cancellationToken)
    {
        await using var command = _createCommand(OracleDdlQueries.ObjectDdl);
        command.AddParameter("object_type", metadataType)
               .AddParameter("object_name", objectName)
               .AddParameter("owner", _schema);

        return await ReadClob(command, cancellationToken);
    }

    /// <summary>
    /// Rebuilds a statement from <c>ALL_SOURCE</c>, <c>ALL_VIEWS</c> or <c>ALL_TRIGGERS</c>. Not as
    /// faithful as <c>DBMS_METADATA</c>, but it only needs the privileges any schema owner has, and
    /// it is the difference between a usable script and no script at all on a locked down database.
    /// </summary>
    private async Task<string?> GetSourceDdl(DbObject dbObject, OracleObjectType type, CancellationToken cancellationToken)
    {
        try
        {
            if (dbObject.Type == "VIEW")
            {
                await using var command = _createCommand(OracleDdlQueries.ViewSource);
                command.AddParameter("owner", _schema).AddParameter("object_name", dbObject.Name);

                var text = await ReadClob(command, cancellationToken);
                return string.IsNullOrWhiteSpace(text)
                    ? null
                    : $"CREATE OR REPLACE FORCE VIEW \"{dbObject.Name}\" AS\n{text.Trim()}";
            }

            if (dbObject.Type == "TRIGGER")
            {
                await using var command = _createCommand(OracleDdlQueries.TriggerSource);
                command.AddParameter("owner", _schema).AddParameter("object_name", dbObject.Name);

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    return null;

                var description = reader.IsDBNull(0) ? null : reader.GetString(0);
                var body = reader.IsDBNull(1) ? null : reader.GetString(1);

                return string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(body)
                    ? null
                    : $"CREATE OR REPLACE TRIGGER {description.Trim()}\n{body.Trim()}";
            }

            if (type.SourceType is null)
                return null;

            var lines = await Query(
                OracleDdlQueries.ObjectSource,
                $"source of {dbObject.Key}",
                r => r.IsDBNull(0) ? string.Empty : r.GetString(0),
                cancellationToken,
                ("owner", _schema), ("object_name", dbObject.Name), ("object_type", type.SourceType));

            if (lines.Count == 0)
                return null;

            // ALL_SOURCE stores the text without the CREATE OR REPLACE prefix.
            return "CREATE OR REPLACE " + string.Concat(lines).TrimEnd();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Could not rebuild {ObjectKey} from the data dictionary", dbObject.Key);
            return null;
        }
    }

    /// <summary>
    /// Comments are not schema objects and <c>DBMS_METADATA</c> only returns them attached to their
    /// table, which is not an option here since the table DDL is emitted on its own.
    /// </summary>
    private async Task WriteComments(StreamWriter writer, IReadOnlyList<DbObject> objects, CancellationToken cancellationToken)
    {
        var commentable = objects.Where(o => o.Type is "TABLE" or "VIEW" or "MATERIALIZED VIEW")
                                 .Select(o => o.Name)
                                 .ToHashSet(StringComparer.Ordinal);

        var tableComments = await TryQuery(
            OracleDdlQueries.TableComments,
            "table comments",
            reader => (Table: reader.GetString(0), Comment: reader.GetString(1)),
            cancellationToken,
            ("owner", _schema));

        var columnComments = await TryQuery(
            OracleDdlQueries.ColumnComments,
            "column comments",
            reader => (Table: reader.GetString(0), Column: reader.GetString(1), Comment: reader.GetString(2)),
            cancellationToken,
            ("owner", _schema));

        var written = 0;

        foreach (var (table, comment) in tableComments.Where(c => commentable.Contains(c.Table)))
        {
            await WriteStatement(writer, $"COMMENT ON TABLE \"{table}\" IS {Literal(comment)}");
            written++;
        }

        foreach (var (table, column, comment) in columnComments.Where(c => commentable.Contains(c.Table)))
        {
            await WriteStatement(writer, $"COMMENT ON COLUMN \"{table}\".\"{column}\" IS {Literal(comment)}");
            written++;
        }

        _logger.LogInformation("Scripted {CommentCount} comments", written);
    }

    private async Task WriteHeader(StreamWriter writer, int objectCount)
    {
        await WriteComment(writer, new string('=', 78));
        await WriteComment(writer, $"Schema {_schema} - {objectCount} object(s)");
        await WriteComment(writer, $"Generated by dbdeploy on {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        await WriteComment(writer, "Do not edit: regenerate instead");
        await WriteComment(writer, new string('=', 78));
        await writer.WriteLineAsync();
    }

    private async Task WriteFooter(StreamWriter writer, OracleObjectsGraph graph)
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

    /// <summary>
    /// Written as SQL*Plus REM lines, which <see cref="OracleScriptParser"/> drops instead of
    /// sending them to the server as an empty statement.
    /// </summary>
    private static async Task WriteComment(StreamWriter writer, string comment)
    {
        foreach (var line in comment.Split('\n'))
            await writer.WriteLineAsync("REM " + line.TrimEnd('\r'));
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
            "Scripted {ScriptedCount}/{ObjectCount} objects into {StatementCount} statements in {Elapsed}: {Breakdown}",
            _scriptedByType.Values.Sum(), objectCount, _statementsWritten, elapsed,
            string.Join(", ", _scriptedByType.OrderBy(kv => OracleObjectType.RankOf(kv.Key)).Select(kv => $"{kv.Key} ({kv.Value})")));

        if (_fallbacksUsed > 0)
        {
            _logger.LogWarning(
                "{FallbackCount} object(s) were rebuilt from the data dictionary because DBMS_METADATA refused them; " +
                "granting SELECT_CATALOG_ROLE to the connected user produces a more faithful script",
                _fallbacksUsed);
        }

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
        _logger.LogDebug("Reading {Description} from schema {SchemaName}", description, _schema);

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
    /// Same as <see cref="Query{T}"/> but a failure is only a warning. Used for the dictionary views
    /// that a restricted user may not be granted on, where losing the result degrades the script
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
            _logger.LogWarning(ex, "Could not read {Description} from schema {SchemaName}; carrying on without it", description, _schema);
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
        return names.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Reads the CLOB through a reader rather than <c>ExecuteScalar</c>, which is what lets a large
    /// package body come back whole.
    /// </summary>
    private static async Task<string?> ReadClob(DbCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        if (await reader.IsDBNullAsync(0, cancellationToken))
            return null;

        using var text = reader.GetTextReader(0);
        return await text.ReadToEndAsync(cancellationToken);
    }

    private static string Literal(string value)
        => "'" + value.Replace("'", "''") + "'";

    private static string Breakdown(IEnumerable<string> types)
        => string.Join(", ", types.GroupBy(t => t)
                                  .OrderBy(g => OracleObjectType.RankOf(g.Key))
                                  .Select(g => $"{g.Key} ({g.Count()})"));

    private static string Describe(Exception ex)
        => ex is OracleException oracle ? $"ORA-{oracle.Number:00000}: {oracle.Message.Trim()}" : ex.Message.Trim();

    /// <summary>
    /// Periodic progress on a long run, so a schema with thousands of objects does not look hung.
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
