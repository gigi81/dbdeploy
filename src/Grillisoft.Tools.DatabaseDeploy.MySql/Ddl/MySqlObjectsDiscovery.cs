using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Database;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.MySql.Ddl;

/// <summary>
/// Works out what has to be scripted, and what has to exist before what, by reading
/// <c>information_schema</c>.
/// </summary>
/// <remarks>
/// MySQL records almost nothing about what depends on what, so most of the ordering is synthesized
/// here: a foreign key needs both its tables, a trigger needs its table, a package body needs its
/// specification. Only the view dependencies come from the server, and only on MySQL 8 - see
/// <see cref="AddViewDependencies"/> for what happens on the servers that do not have them.
/// </remarks>
internal sealed class MySqlObjectsDiscovery
{
    /// <summary>
    /// MySQL folds identifier case according to <c>lower_case_table_names</c>, which differs
    /// between a Linux server and a Windows or macOS one, so nothing here may compare names
    /// ordinally.
    /// </summary>
    private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

    private readonly CatalogReader _catalog;
    private readonly string _database;
    private readonly string _migrationTable;
    private readonly ILogger _logger;

    public MySqlObjectsDiscovery(CatalogReader catalog, string database, string migrationTable, ILogger logger)
    {
        _catalog = catalog;
        _database = database;
        _migrationTable = migrationTable.Unqualified();
        _logger = logger;
    }

    public async Task<(List<DbObject> Objects, List<(DbObject DbObject, DbObject DependsOn)> Dependencies)> Discover(
        CancellationToken cancellationToken)
    {
        await LogUnsupportedObjectTypes(cancellationToken);

        var excluded = await GetExcludedNames(cancellationToken);
        var objects = new List<DbObject>();

        await AddNamed(objects, MySqlDdlQueries.Sequences, MySqlObjectType.Sequence, "sequences", excluded, cancellationToken);
        await AddNamed(objects, MySqlDdlQueries.Tables, MySqlObjectType.Table, "tables", excluded, cancellationToken);
        await AddNamed(objects, MySqlDdlQueries.Views, MySqlObjectType.View, "views", excluded, cancellationToken);
        await AddRoutines(objects, excluded, cancellationToken);
        await AddEvents(objects, excluded, cancellationToken);

        var dependencies = new List<(DbObject DbObject, DbObject DependsOn)>();

        await AddTriggers(objects, dependencies, excluded, cancellationToken);
        await AddForeignKeys(objects, dependencies, excluded, cancellationToken);
        await AddViewDependencies(objects, dependencies, cancellationToken);
        AddBodyDependencies(objects, dependencies);

        _logger.LogInformation("Scripting {ObjectCount} objects: {Breakdown}",
            objects.Count, objects.Select(o => o.Type).Breakdown(MySqlObjectType.RankOf));

        return (objects, dependencies);
    }

    /// <summary>
    /// The migrations table itself, and everything hanging off it. dbdeploy manages that table, so
    /// a script that recreates it would fight with the tool on the next deployment.
    /// </summary>
    private async Task<HashSet<string>> GetExcludedNames(CancellationToken cancellationToken)
    {
        var excluded = await _catalog.TryQueryNames(
            MySqlDdlQueries.MigrationTableObjects,
            "objects belonging to the migrations table",
            NameComparer,
            cancellationToken,
            ("migration_table", _migrationTable));

        excluded.Add(_migrationTable);
        return excluded;
    }

    private async Task AddNamed(
        List<DbObject> objects,
        string sql,
        string type,
        string description,
        HashSet<string> excluded,
        CancellationToken cancellationToken)
    {
        var names = await _catalog.TryQuery(sql, description, reader => reader.GetString(0), cancellationToken);

        foreach (var name in names)
        {
            if (excluded.Contains(name))
            {
                _logger.LogDebug("Skipping {Type} {Name}: belongs to the migrations table", type, name);
                continue;
            }

            objects.Add(new DbObject(name, type));
        }

        _logger.LogDebug("Found {Count} {Description}", names.Count, description);
    }

    private async Task AddRoutines(List<DbObject> objects, HashSet<string> excluded, CancellationToken cancellationToken)
    {
        var routines = await _catalog.TryQuery(
            MySqlDdlQueries.Routines,
            "routines",
            reader => (Name: reader.GetString(0), Type: reader.GetString(1)),
            cancellationToken);

        foreach (var (name, type) in routines)
        {
            if (MySqlObjectType.Find(type) is null)
            {
                _logger.LogWarning("Skipping routine {Name}: {Type} is not a type this tool can script", name, type);
                continue;
            }

            if (excluded.Contains(name))
                continue;

            objects.Add(new DbObject(name, type.ToUpperInvariant()));
        }
    }

    private async Task AddEvents(List<DbObject> objects, HashSet<string> excluded, CancellationToken cancellationToken)
        => await AddNamed(objects, MySqlDdlQueries.Events, MySqlObjectType.Event, "events", excluded, cancellationToken);

    /// <summary>
    /// Triggers, and the link to the table each one sits on. A trigger cannot be created before its
    /// table, and nothing in <c>information_schema</c> records that.
    /// </summary>
    private async Task AddTriggers(
        List<DbObject> objects,
        List<(DbObject DbObject, DbObject DependsOn)> dependencies,
        HashSet<string> excluded,
        CancellationToken cancellationToken)
    {
        var triggers = await _catalog.TryQuery(
            MySqlDdlQueries.Triggers,
            "triggers",
            reader => (Name: reader.GetString(0), Table: reader.GetString(1)),
            cancellationToken);

        var tables = ByName(objects, MySqlObjectType.Table);

        foreach (var (name, table) in triggers)
        {
            if (excluded.Contains(name) || excluded.Contains(table))
            {
                _logger.LogDebug("Skipping trigger {Name}: table {Table} is not being scripted", name, table);
                continue;
            }

            var trigger = new DbObject(name, MySqlObjectType.Trigger);
            objects.Add(trigger);

            if (tables.TryGetValue(table, out var target))
                dependencies.Add((trigger, target));
        }
    }

    /// <summary>
    /// Foreign keys are not objects of their own in MySQL, so they are synthesized here and made to
    /// depend on both tables. That is what pushes them past every <c>CREATE TABLE</c> in the script
    /// - and what lets two tables reference each other, which no ordering of inline keys could.
    /// </summary>
    private async Task AddForeignKeys(
        List<DbObject> objects,
        List<(DbObject DbObject, DbObject DependsOn)> dependencies,
        HashSet<string> excluded,
        CancellationToken cancellationToken)
    {
        var foreignKeys = await _catalog.TryQuery(
            MySqlDdlQueries.ForeignKeys,
            "foreign keys",
            reader => (Name: reader.GetString(0), Table: reader.GetString(1),
                       Referenced: reader.IsDBNull(2) ? null : reader.GetString(2)),
            cancellationToken);

        var tables = ByName(objects, MySqlObjectType.Table);
        var added = 0;

        foreach (var (name, table, referenced) in foreignKeys)
        {
            if (excluded.Contains(name) || excluded.Contains(table) ||
                (referenced is not null && excluded.Contains(referenced)) ||
                !tables.ContainsKey(table))
            {
                _logger.LogDebug("Skipping foreign key {Name}: table {Table} is not being scripted", name, table);
                continue;
            }

            // The key is addressed through its table: a constraint name is only unique per table.
            var constraint = new DbObject($"{table}.{name}", MySqlObjectType.ForeignKey);
            objects.Add(constraint);
            added++;

            foreach (var dependency in new[] { table, referenced })
            {
                if (dependency is not null && tables.TryGetValue(dependency, out var target))
                    dependencies.Add((constraint, target));
            }
        }

        _logger.LogInformation("Found {ForeignKeyCount} foreign keys", added);
    }

    /// <summary>
    /// What a view is built on, so a view selecting from another view comes out after it.
    /// </summary>
    /// <remarks>
    /// <c>VIEW_TABLE_USAGE</c> and <c>VIEW_ROUTINE_USAGE</c> are MySQL 8 views that MariaDB does
    /// not have. Falling back to looking for each object's quoted name in the text of every view is
    /// crude and will find a name inside a string literal, but a dependency that is not real only
    /// costs ordering, and <see cref="DbObjectsGraph"/> breaks a cycle rather than failing. Missing
    /// the dependency, by contrast, costs a script that does not replay.
    /// </remarks>
    private async Task AddViewDependencies(
        List<DbObject> objects,
        List<(DbObject DbObject, DbObject DependsOn)> dependencies,
        CancellationToken cancellationToken)
    {
        var views = ByName(objects, MySqlObjectType.View);
        if (views.Count == 0)
            return;

        var byName = ByName(objects, MySqlObjectType.Table, MySqlObjectType.View,
            MySqlObjectType.Function, MySqlObjectType.Sequence);

        var declared = await _catalog.TryQuery(
            MySqlDdlQueries.ViewTableUsage,
            "view dependencies",
            reader => (View: reader.GetString(0), Used: reader.GetString(1)),
            cancellationToken);

        declared.AddRange(await _catalog.TryQuery(
            MySqlDdlQueries.ViewRoutineUsage,
            "view routine dependencies",
            reader => (View: reader.GetString(0), Used: reader.GetString(1)),
            cancellationToken));

        if (declared.Count > 0)
        {
            foreach (var (view, used) in declared)
            {
                if (views.TryGetValue(view, out var dependent) && byName.TryGetValue(used, out var target))
                    dependencies.Add((dependent, target));
            }

            _logger.LogDebug("Found {Count} view dependencies declared by the server", declared.Count);
            return;
        }

        _logger.LogDebug("The server does not report view dependencies; reading them out of the view definitions");
        await AddViewDependenciesFromDefinitions(views, byName, dependencies, cancellationToken);
    }

    private async Task AddViewDependenciesFromDefinitions(
        Dictionary<string, DbObject> views,
        Dictionary<string, DbObject> byName,
        List<(DbObject DbObject, DbObject DependsOn)> dependencies,
        CancellationToken cancellationToken)
    {
        var definitions = await _catalog.TryQuery(
            MySqlDdlQueries.ViewDefinitions,
            "view definitions",
            reader => (View: reader.GetString(0), Sql: reader.IsDBNull(1) ? string.Empty : reader.GetString(1)),
            cancellationToken);

        foreach (var (view, sql) in definitions)
        {
            if (!views.TryGetValue(view, out var dependent))
                continue;

            foreach (var (name, target) in byName)
            {
                if (target.Equals(dependent))
                    continue;

                if (sql.Contains(name.Quote(), StringComparison.OrdinalIgnoreCase))
                    dependencies.Add((dependent, target));
            }
        }
    }

    /// <summary>
    /// A MariaDB package body cannot compile before its specification.
    /// </summary>
    private static void AddBodyDependencies(
        List<DbObject> objects,
        List<(DbObject DbObject, DbObject DependsOn)> dependencies)
    {
        var specs = ByName(objects, MySqlObjectType.Package);

        foreach (var body in objects.Where(o => o.Type == MySqlObjectType.PackageBody))
        {
            if (specs.TryGetValue(body.Name, out var spec))
                dependencies.Add((body, spec));
        }
    }

    private async Task LogUnsupportedObjectTypes(CancellationToken cancellationToken)
    {
        var unsupported = await _catalog.TryQuery(
            MySqlDdlQueries.UnsupportedObjectTypes,
            "unsupported object types",
            reader => (Type: reader.GetString(0), Count: Convert.ToInt32(reader.GetValue(1))),
            cancellationToken);

        if (unsupported.Count == 0)
            return;

        _logger.LogWarning(
            "Database {DatabaseName} contains {TypeCount} object type(s) that are not scripted: {Types}",
            _database, unsupported.Count,
            string.Join(", ", unsupported.Select(u => $"{u.Type} ({u.Count})")));
    }

    /// <summary>Objects of the given types indexed by name, tolerating duplicates.</summary>
    private static Dictionary<string, DbObject> ByName(IEnumerable<DbObject> objects, params string[] types)
    {
        var result = new Dictionary<string, DbObject>(NameComparer);

        foreach (var dbObject in objects.Where(o => types.Contains(o.Type, StringComparer.OrdinalIgnoreCase)))
            result.TryAdd(dbObject.Name, dbObject);

        return result;
    }
}
