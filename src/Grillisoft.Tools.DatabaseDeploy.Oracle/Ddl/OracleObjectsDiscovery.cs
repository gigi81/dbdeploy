using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Database;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle.Ddl;

/// <summary>
/// Works out what has to be scripted, and what has to exist before what, by reading the
/// <c>ALL_*</c> data dictionary views.
/// </summary>
/// <remarks>
/// <c>ALL_DEPENDENCIES</c> only knows about the things Oracle compiles - views, program units,
/// triggers - so the rest of the ordering is synthesized here: an index needs its table, a foreign
/// key needs both tables, a body needs its specification. The same pass drops the objects that must
/// not be scripted at all, which is mostly tables that only exist to store something else.
/// </remarks>
internal sealed class OracleObjectsDiscovery
{
    private readonly CatalogReader _catalog;
    private readonly string _schema;
    private readonly string _migrationTable;
    private readonly ILogger _logger;

    public OracleObjectsDiscovery(CatalogReader catalog, string schema, string migrationTable, ILogger logger)
    {
        _catalog = catalog;
        _schema = schema;
        _migrationTable = migrationTable;
        _logger = logger;
    }

    public async Task<(List<DbObject> Objects, List<OracleObjectDependencies> Dependencies)> Discover(
        CancellationToken cancellationToken)
    {
        await LogUnsupportedObjectTypes(cancellationToken);

        var excludedTables = await GetExcludedTables(cancellationToken);
        var excludedNames = await TryQueryNames(
            OracleDdlQueries.MigrationTableObjects, "objects belonging to the migrations table", cancellationToken,
            ("owner", _schema), ("migration_table", _migrationTable));

        excludedNames.Add(_migrationTable);

        var objects = await _catalog.Query(
            OracleDdlQueries.Objects,
            "schema objects",
            reader => new DbObject(reader.GetString(0), reader.GetString(1)),
            cancellationToken,
            ("owner", _schema));

        _logger.LogInformation("Found {ObjectCount} objects in schema {SchemaName}: {Breakdown}",
            objects.Count, _schema, objects.Select(o => o.Type).Breakdown(OracleObjectType.RankOf));

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

        _logger.LogInformation("Scripting {ObjectCount} objects: {Breakdown}",
            final.Count, final.Select(o => o.Type).Breakdown(OracleObjectType.RankOf));

        return (final, dependencies);
    }

    /// <summary>
    /// The comments on the tables, views and materialized views being scripted.
    /// </summary>
    /// <remarks>
    /// Comments are not schema objects and <c>DBMS_METADATA</c> only returns them attached to their
    /// table, which is not an option here since the table DDL is emitted on its own.
    /// </remarks>
    public async Task<List<(string Table, string? Column, string Comment)>> GetComments(
        IReadOnlyList<DbObject> objects,
        CancellationToken cancellationToken)
    {
        var commentable = objects.Where(o => o.Type is "TABLE" or "VIEW" or "MATERIALIZED VIEW")
                                 .Select(o => o.Name)
                                 .ToHashSet(StringComparer.Ordinal);

        var tableComments = await _catalog.TryQuery(
            OracleDdlQueries.TableComments,
            "table comments",
            reader => (Table: reader.GetString(0), Comment: reader.GetString(1)),
            cancellationToken,
            ("owner", _schema));

        var columnComments = await _catalog.TryQuery(
            OracleDdlQueries.ColumnComments,
            "column comments",
            reader => (Table: reader.GetString(0), Column: reader.GetString(1), Comment: reader.GetString(2)),
            cancellationToken,
            ("owner", _schema));

        var comments = new List<(string Table, string? Column, string Comment)>();

        comments.AddRange(tableComments.Where(c => commentable.Contains(c.Table))
                                       .Select(c => (c.Table, (string?)null, c.Comment)));

        comments.AddRange(columnComments.Where(c => commentable.Contains(c.Table))
                                        .Select(c => (c.Table, (string?)c.Column, c.Comment)));

        return comments;
    }

    /// <summary>
    /// Objects living in the schema that this tool cannot script. They are not an error, but they
    /// are the first thing to check when a deployment of the generated script comes up short.
    /// </summary>
    private async Task LogUnsupportedObjectTypes(CancellationToken cancellationToken)
    {
        var unsupported = await _catalog.TryQuery(
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
        var dependencies = await _catalog.Query(
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

        var indexes = await _catalog.TryQuery(
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
        var foreignKeys = await _catalog.TryQuery(
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
        var triggers = await _catalog.TryQuery(
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

    /// <summary>Oracle folds unquoted names to upper case, so the dictionary compares them ordinally.</summary>
    private Task<HashSet<string>> TryQueryNames(
        string sql,
        string description,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
        => _catalog.TryQueryNames(sql, description, StringComparer.Ordinal, cancellationToken, parameters);
}
