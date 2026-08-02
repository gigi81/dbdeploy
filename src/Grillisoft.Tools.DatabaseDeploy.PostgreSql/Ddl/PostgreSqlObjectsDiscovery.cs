using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Database;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.PostgreSql.Ddl;

/// <summary>
/// Works out what has to be scripted, and what has to exist before what, by reading
/// <c>pg_catalog</c>.
/// </summary>
/// <remarks>
/// PostgreSQL records dependencies properly, in <c>pg_depend</c>, which is most of the ordering:
/// a view on a view, a view calling a function, a column of an enum, a function returning a table's
/// row type. What is synthesized here is the rest - an index needs its table, a constraint needs
/// both of its, a trigger needs its table and its function, everything needs its schema.
/// <para>
/// The <c>pg_depend</c> reads are typed queries rather than one walk over
/// <c>pg_identify_object</c>, because the generic identity of a view's dependency is its rewrite
/// rule rather than the view, and unpicking that afterwards is worse than asking three precise
/// questions.
/// </para>
/// </remarks>
internal sealed class PostgreSqlObjectsDiscovery
{
    private readonly CatalogReader _catalog;
    private readonly string _database;
    private readonly string _migrationTable;
    private readonly string _migrationSchema;
    private readonly ILogger _logger;

    private readonly Dictionary<DbObject, PostgreSqlObject> _objects = [];
    private readonly Dictionary<uint, PostgreSqlRelationOptions> _relationOptions = [];
    private readonly Dictionary<(string Schema, string Name), List<string>> _inherits = [];

    public PostgreSqlObjectsDiscovery(CatalogReader catalog, string database, string migrationTable, ILogger logger)
    {
        _catalog = catalog;
        _database = database;
        _migrationTable = migrationTable.Unqualified();
        _migrationSchema = migrationTable.SchemaOf() ?? "public";
        _logger = logger;
    }

    /// <summary>The object behind a <see cref="DbObject"/>, for the scripter.</summary>
    public PostgreSqlObject? Find(DbObject dbObject) => _objects.GetValueOrDefault(dbObject);

    /// <summary>The <c>pg_class</c> row of a relation, read once during discovery.</summary>
    public PostgreSqlRelationOptions OptionsOf(uint oid) =>
        _relationOptions.GetValueOrDefault(oid,
            new PostgreSqlRelationOptions('p', string.Empty, string.Empty, string.Empty, false));

    /// <summary>The qualified names of the tables a table inherits, in declaration order.</summary>
    public IReadOnlyList<string> InheritedBy(PostgreSqlObject table) =>
        _inherits.GetValueOrDefault((table.Schema, table.Name), []);

    public async Task<(List<DbObject> Objects, List<(DbObject DbObject, DbObject DependsOn)> Dependencies)> Discover(
        CancellationToken cancellationToken)
    {
        await LogUnsupportedObjectTypes(cancellationToken);

        var dependencies = new List<(DbObject DbObject, DbObject DependsOn)>();

        var schemas = await AddSchemas(cancellationToken);
        await AddTypes(cancellationToken);
        await AddRoutines(cancellationToken);
        await AddRelations(cancellationToken);
        await AddConstraints(cancellationToken);
        await AddTriggers(dependencies, cancellationToken);
        await AddRules(cancellationToken);
        await AddPartitionsAndOwners(cancellationToken);

        await ReadInheritance(cancellationToken);
        AddParentDependencies(dependencies);
        AddSchemaDependencies(schemas, dependencies);
        await AddDeclaredDependencies(dependencies, cancellationToken);

        var objects = _objects.Keys.ToList();

        _logger.LogInformation("Scripting {ObjectCount} objects: {Breakdown}",
            objects.Count, objects.Select(o => o.Type).Breakdown(PostgreSqlObjectType.RankOf));

        return (objects, dependencies);
    }

    private void Add(PostgreSqlObject dbObject) => _objects.TryAdd(dbObject.DbObject, dbObject);

    /// <summary>
    /// Whether an object belongs to the migrations table, which dbdeploy owns and a generated
    /// script must not recreate.
    /// </summary>
    private bool IsMigrationTable(string schema, string name)
        => string.Equals(name, _migrationTable, StringComparison.Ordinal)
           && string.Equals(schema, _migrationSchema, StringComparison.Ordinal);

    private async Task<List<string>> AddSchemas(CancellationToken cancellationToken)
    {
        var schemas = await _catalog.Query(
            PostgreSqlDdlQueries.Schemas,
            "schemas",
            reader => (Oid: reader.GetFieldValue<uint>(0), Name: reader.GetString(1)),
            cancellationToken);

        foreach (var (oid, name) in schemas)
        {
            // public exists in every new database, exactly as dbo does on SQL Server.
            if (string.Equals(name, "public", StringComparison.Ordinal))
                continue;

            Add(new PostgreSqlObject(PostgreSqlObjectType.Schema, oid, name, name));
        }

        return schemas.Select(s => s.Name).ToList();
    }

    private async Task AddTypes(CancellationToken cancellationToken)
    {
        var types = await _catalog.Query(
            PostgreSqlDdlQueries.Types,
            "types",
            reader => (
                Oid: reader.GetFieldValue<uint>(0),
                Schema: reader.GetString(1),
                Name: reader.GetString(2),
                Kind: reader.GetString(3)),
            cancellationToken);

        foreach (var (oid, schema, name, kind) in types)
        {
            if (PostgreSqlObjectType.FromTypType(kind[0]) is not { } type)
                continue;

            Add(new PostgreSqlObject(type, oid, schema, name, detail: kind));
        }
    }

    private async Task AddRoutines(CancellationToken cancellationToken)
    {
        var routines = await _catalog.Query(
            PostgreSqlDdlQueries.Routines,
            "routines",
            reader => (
                Oid: reader.GetFieldValue<uint>(0),
                Schema: reader.GetString(1),
                Name: reader.GetString(2),
                Kind: reader.GetString(3),
                Arguments: reader.GetString(4)),
            cancellationToken);

        foreach (var (oid, schema, name, kind, arguments) in routines)
        {
            if (PostgreSqlObjectType.FromProKind(kind[0]) is not { } type)
            {
                _logger.LogDebug("Skipping routine {Schema}.{Name}: kind {Kind} is not scripted", schema, name, kind);
                continue;
            }

            Add(new PostgreSqlObject(type, oid, schema, name, arguments));
        }
    }

    private async Task AddRelations(CancellationToken cancellationToken)
    {
        var relations = await _catalog.Query(
            PostgreSqlDdlQueries.Relations,
            "relations",
            reader => (
                Oid: reader.GetFieldValue<uint>(0),
                Schema: reader.GetString(1),
                Name: reader.GetString(2),
                Kind: reader.GetString(3),
                Persistence: reader.GetString(4),
                IsPartition: reader.GetBoolean(5),
                Options: reader.GetString(6),
                Bound: reader.GetString(7),
                PartitionKey: reader.GetString(8)),
            cancellationToken);

        foreach (var relation in relations)
        {
            if (IsMigrationTable(relation.Schema, relation.Name))
            {
                _logger.LogDebug("Skipping {Schema}.{Name}: it is the migrations table", relation.Schema, relation.Name);
                continue;
            }

            if (PostgreSqlObjectType.FromRelKind(relation.Kind[0]) is not { } type)
                continue;

            _relationOptions[relation.Oid] = new PostgreSqlRelationOptions(
                relation.Persistence[0], relation.Options, relation.PartitionKey, relation.Bound,
                relation.IsPartition);

            if (type == PostgreSqlObjectType.Index)
            {
                // An index is addressed through its table: the name is only unique within it.
                Add(new PostgreSqlObject(type, relation.Oid, relation.Schema, relation.Name,
                    parentSchema: relation.Schema, parentName: await TableOfIndex(relation.Oid, cancellationToken)));
                continue;
            }

            Add(new PostgreSqlObject(type, relation.Oid, relation.Schema, relation.Name));
        }
    }

    /// <summary>The table an index sits on, which <c>pg_get_indexdef</c> does not have to be told.</summary>
    private async Task<string?> TableOfIndex(uint oid, CancellationToken cancellationToken)
    {
        var tables = await _catalog.TryQuery(
            PostgreSqlDdlQueries.IndexTable,
            $"table of index {oid}",
            reader => reader.GetString(0),
            cancellationToken,
            ("oid", (long)oid));

        return tables.FirstOrDefault();
    }

    private async Task AddConstraints(CancellationToken cancellationToken)
    {
        var constraints = await _catalog.Query(
            PostgreSqlDdlQueries.Constraints,
            "constraints",
            reader => (
                Oid: reader.GetFieldValue<uint>(0),
                Schema: reader.GetString(1),
                Table: reader.GetString(2),
                Name: reader.GetString(3),
                Kind: reader.GetString(4),
                RefSchema: reader.GetString(5),
                RefTable: reader.GetString(6)),
            cancellationToken);

        foreach (var constraint in constraints)
        {
            if (IsMigrationTable(constraint.Schema, constraint.Table))
                continue;

            var type = constraint.Kind[0] == 'f'
                ? PostgreSqlObjectType.ForeignKey
                : PostgreSqlObjectType.Constraint;

            Add(new PostgreSqlObject(type, constraint.Oid, constraint.Schema, constraint.Name,
                parentSchema: constraint.Schema, parentName: constraint.Table,
                detail: constraint.RefTable.Length == 0 ? null : constraint.RefTable.Qualify(constraint.RefSchema)));
        }
    }

    private async Task AddTriggers(
        List<(DbObject DbObject, DbObject DependsOn)> dependencies,
        CancellationToken cancellationToken)
    {
        var triggers = await _catalog.TryQuery(
            PostgreSqlDdlQueries.Triggers,
            "triggers",
            reader => (
                Oid: reader.GetFieldValue<uint>(0),
                Schema: reader.GetString(1),
                Table: reader.GetString(2),
                Name: reader.GetString(3),
                FunctionSchema: reader.GetString(4),
                FunctionName: reader.GetString(5),
                FunctionArguments: reader.GetString(6)),
            cancellationToken);

        foreach (var trigger in triggers)
        {
            if (IsMigrationTable(trigger.Schema, trigger.Table))
                continue;

            var dbObject = new PostgreSqlObject(PostgreSqlObjectType.Trigger, trigger.Oid, trigger.Schema, trigger.Name,
                parentSchema: trigger.Schema, parentName: trigger.Table);

            Add(dbObject);

            // A trigger needs the function it calls as much as it needs its table.
            var function = new DbObject(
                $"{trigger.FunctionName.Qualify(trigger.FunctionSchema)}({trigger.FunctionArguments})",
                PostgreSqlObjectType.Function);

            dependencies.Add((dbObject.DbObject, function));
        }
    }

    private async Task AddRules(CancellationToken cancellationToken)
    {
        var rules = await _catalog.TryQuery(
            PostgreSqlDdlQueries.Rules,
            "rules",
            reader => (
                Oid: reader.GetFieldValue<uint>(0),
                Schema: reader.GetString(1),
                Table: reader.GetString(2),
                Name: reader.GetString(3)),
            cancellationToken);

        foreach (var rule in rules)
        {
            if (IsMigrationTable(rule.Schema, rule.Table))
                continue;

            Add(new PostgreSqlObject(PostgreSqlObjectType.Rule, rule.Oid, rule.Schema, rule.Name,
                parentSchema: rule.Schema, parentName: rule.Table));
        }
    }

    /// <summary>
    /// The two statements that tie two objects together and so cannot belong to either: the
    /// <c>ATTACH PARTITION</c> of a partition, and the <c>OWNED BY</c> of a serial column's
    /// sequence.
    /// </summary>
    private async Task AddPartitionsAndOwners(CancellationToken cancellationToken)
    {
        var partitions = await _catalog.TryQuery(
            PostgreSqlDdlQueries.Partitions,
            "partitions",
            reader => (
                Schema: reader.GetString(0),
                Name: reader.GetString(1),
                ParentSchema: reader.GetString(2),
                ParentName: reader.GetString(3),
                Bound: reader.GetString(4)),
            cancellationToken);

        foreach (var partition in partitions)
        {
            Add(new PostgreSqlObject(PostgreSqlObjectType.Partition, 0, partition.Schema, partition.Name,
                parentSchema: partition.ParentSchema, parentName: partition.ParentName,
                detail: partition.Bound));
        }

        var owners = await _catalog.TryQuery(
            PostgreSqlDdlQueries.SequenceOwners,
            "sequence owners",
            reader => (
                Schema: reader.GetString(0),
                Name: reader.GetString(1),
                TableSchema: reader.GetString(2),
                TableName: reader.GetString(3),
                Column: reader.GetString(4)),
            cancellationToken);

        foreach (var owner in owners)
        {
            if (IsMigrationTable(owner.TableSchema, owner.TableName))
                continue;

            Add(new PostgreSqlObject(PostgreSqlObjectType.SequenceOwner, 0, owner.Schema, owner.Name,
                parentSchema: owner.TableSchema, parentName: owner.TableName,
                detail: owner.Column));
        }
    }

    private async Task ReadInheritance(CancellationToken cancellationToken)
    {
        var inherits = await _catalog.TryQuery(
            PostgreSqlDdlQueries.Inheritance,
            "table inheritance",
            reader => (
                Schema: reader.GetString(0),
                Name: reader.GetString(1),
                ParentSchema: reader.GetString(2),
                ParentName: reader.GetString(3)),
            cancellationToken);

        foreach (var (schema, name, parentSchema, parentName) in inherits)
        {
            if (!_inherits.TryGetValue((schema, name), out var parents))
                _inherits[(schema, name)] = parents = [];

            parents.Add(parentName.Qualify(parentSchema));
        }
    }

    /// <summary>
    /// The links that come from the shape of the objects rather than from <c>pg_depend</c>: an
    /// index, a constraint, a trigger or a rule needs the relation it hangs off; a foreign key
    /// needs the table it points at too; the two synthesized statements need both of their ends;
    /// and a child table needs the tables it inherits.
    /// </summary>
    private void AddParentDependencies(List<(DbObject DbObject, DbObject DependsOn)> dependencies)
    {
        foreach (var dbObject in _objects.Values)
        {
            foreach (var parent in ParentsOf(dbObject))
            {
                foreach (var type in RelationTypes)
                    dependencies.Add((dbObject.DbObject, new DbObject(parent, type)));
            }
        }

        foreach (var ((schema, name), parents) in _inherits)
        {
            var child = new DbObject(name.Qualify(schema), PostgreSqlObjectType.Table);

            foreach (var parent in parents)
                dependencies.Add((child, new DbObject(parent, PostgreSqlObjectType.Table)));
        }
    }

    /// <summary>
    /// The relations a dependency edge might point at. The catalog knows which one it is; the graph
    /// throws away the edges whose target is not being scripted, so naming all of them costs a
    /// handful of ignored edges and saves a lookup per object.
    /// </summary>
    private static readonly string[] RelationTypes =
    [
        PostgreSqlObjectType.Table,
        PostgreSqlObjectType.View,
        PostgreSqlObjectType.MaterializedView,
        PostgreSqlObjectType.Sequence,
    ];

    private static IEnumerable<string> ParentsOf(PostgreSqlObject dbObject)
    {
        if (dbObject.ParentName is not null)
            yield return dbObject.QualifiedParent;

        // A foreign key also needs the table it references, and a sequence owner the sequence.
        if (dbObject.Type == PostgreSqlObjectType.ForeignKey && dbObject.Detail is { } referenced)
            yield return referenced;

        if (dbObject.Type is PostgreSqlObjectType.Partition or PostgreSqlObjectType.SequenceOwner)
            yield return dbObject.QualifiedName;
    }

    /// <summary>Everything in a schema needs the schema.</summary>
    private void AddSchemaDependencies(
        List<string> schemas,
        List<(DbObject DbObject, DbObject DependsOn)> dependencies)
    {
        var inNamedSchema = _objects.Values.Where(o =>
            o.Type != PostgreSqlObjectType.Schema && schemas.Contains(o.Schema, StringComparer.Ordinal));

        foreach (var dbObject in inNamedSchema)
            dependencies.Add((dbObject.DbObject, new DbObject(dbObject.Schema.Quote(), PostgreSqlObjectType.Schema)));
    }

    /// <summary>
    /// The dependencies the server records: what a view is built on, what it calls, the user
    /// defined type of a column, the types a routine's signature names, and the sequence a serial
    /// default calls.
    /// </summary>
    private async Task AddDeclaredDependencies(
        List<(DbObject DbObject, DbObject DependsOn)> dependencies,
        CancellationToken cancellationToken)
    {
        var views = await _catalog.TryQuery(
            PostgreSqlDdlQueries.ViewDependencies,
            "view dependencies",
            reader => (
                Schema: reader.GetString(0), Name: reader.GetString(1), Kind: reader.GetString(2),
                RefSchema: reader.GetString(3), RefName: reader.GetString(4), RefKind: reader.GetString(5)),
            cancellationToken);

        foreach (var view in views)
        {
            AddIfKnown(dependencies,
                view.Name.Qualify(view.Schema), PostgreSqlObjectType.FromRelKind(view.Kind[0]),
                view.RefName.Qualify(view.RefSchema), PostgreSqlObjectType.FromRelKind(view.RefKind[0]));
        }

        var viewRoutines = await _catalog.TryQuery(
            PostgreSqlDdlQueries.ViewRoutineDependencies,
            "view routine dependencies",
            reader => (
                Schema: reader.GetString(0), Name: reader.GetString(1), Kind: reader.GetString(2),
                RefSchema: reader.GetString(3), RefName: reader.GetString(4),
                RefArguments: reader.GetString(5), RefKind: reader.GetString(6)),
            cancellationToken);

        foreach (var view in viewRoutines)
        {
            AddIfKnown(dependencies,
                view.Name.Qualify(view.Schema), PostgreSqlObjectType.FromRelKind(view.Kind[0]),
                $"{view.RefName.Qualify(view.RefSchema)}({view.RefArguments})",
                PostgreSqlObjectType.FromProKind(view.RefKind[0]));
        }

        var columnTypes = await _catalog.TryQuery(
            PostgreSqlDdlQueries.ColumnTypeDependencies,
            "column type dependencies",
            reader => (
                Schema: reader.GetString(0), Name: reader.GetString(1), Kind: reader.GetString(2),
                TypeSchema: reader.GetString(3), TypeName: reader.GetString(4), TypeKind: reader.GetString(5)),
            cancellationToken);

        foreach (var column in columnTypes)
        {
            AddIfKnown(dependencies,
                column.Name.Qualify(column.Schema), PostgreSqlObjectType.FromRelKind(column.Kind[0]),
                column.TypeName.Qualify(column.TypeSchema), PostgreSqlObjectType.FromTypType(column.TypeKind[0]));
        }

        var routineTypes = await _catalog.TryQuery(
            PostgreSqlDdlQueries.RoutineTypeDependencies,
            "routine type dependencies",
            reader => (
                Schema: reader.GetString(0), Name: reader.GetString(1), Arguments: reader.GetString(2),
                Kind: reader.GetString(3),
                TypeSchema: reader.GetString(4), TypeName: reader.GetString(5), TypeKind: reader.GetString(6),
                RelName: reader.GetString(7), RelKind: reader.GetString(8), RelSchema: reader.GetString(9)),
            cancellationToken);

        foreach (var routine in routineTypes)
        {
            var dependent = $"{routine.Name.Qualify(routine.Schema)}({routine.Arguments})";
            var dependentType = PostgreSqlObjectType.FromProKind(routine.Kind[0]);

            // A function returning SETOF customer depends on the customer table, not on a type.
            if (routine.RelName.Length > 0)
            {
                AddIfKnown(dependencies, dependent, dependentType,
                    routine.RelName.Qualify(routine.RelSchema), PostgreSqlObjectType.FromRelKind(routine.RelKind[0]));
                continue;
            }

            AddIfKnown(dependencies, dependent, dependentType,
                routine.TypeName.Qualify(routine.TypeSchema), PostgreSqlObjectType.FromTypType(routine.TypeKind[0]));
        }

        var sequences = await _catalog.TryQuery(
            PostgreSqlDdlQueries.ColumnSequenceDependencies,
            "column sequence dependencies",
            reader => (
                Schema: reader.GetString(0), Name: reader.GetString(1),
                SequenceSchema: reader.GetString(2), SequenceName: reader.GetString(3)),
            cancellationToken);

        foreach (var sequence in sequences)
        {
            AddIfKnown(dependencies,
                sequence.Name.Qualify(sequence.Schema), PostgreSqlObjectType.Table,
                sequence.SequenceName.Qualify(sequence.SequenceSchema), PostgreSqlObjectType.Sequence);
        }
    }

    private static void AddIfKnown(
        List<(DbObject DbObject, DbObject DependsOn)> dependencies,
        string name,
        string? type,
        string dependsOnName,
        string? dependsOnType)
    {
        if (type is null || dependsOnType is null)
            return;

        dependencies.Add((new DbObject(name, type), new DbObject(dependsOnName, dependsOnType)));
    }

    private async Task LogUnsupportedObjectTypes(CancellationToken cancellationToken)
    {
        var unsupported = await _catalog.TryQuery(
            PostgreSqlDdlQueries.UnsupportedObjectTypes,
            "unsupported object types",
            reader => (Type: reader.GetString(0), Count: Convert.ToInt32(reader.GetValue(1))),
            cancellationToken);

        var present = unsupported.Where(u => u.Count > 0).ToList();
        if (present.Count == 0)
            return;

        _logger.LogWarning(
            "Database {DatabaseName} contains {TypeCount} object type(s) that are not scripted: {Types}",
            _database, present.Count,
            string.Join(", ", present.Select(u => $"{u.Type} ({u.Count})")));
    }
}
