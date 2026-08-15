using Grillisoft.Tools.DatabaseDeploy.Database;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.PostgreSql.Ddl;

/// <summary>
/// Turns one object into the statement that creates it.
/// </summary>
/// <remarks>
/// PostgreSQL ships a scripting function for most kinds of object - <c>pg_get_viewdef</c>,
/// <c>pg_get_indexdef</c>, <c>pg_get_constraintdef</c>, <c>pg_get_functiondef</c>,
/// <c>pg_get_triggerdef</c> - and this uses every one of them, because the server's own answer is
/// always going to beat a reimplementation. What it has no function for is <c>CREATE TABLE</c>,
/// <c>CREATE TYPE</c>, <c>CREATE DOMAIN</c>, <c>CREATE SEQUENCE</c> and <c>CREATE AGGREGATE</c>,
/// and those are assembled by the three scripters next door.
/// </remarks>
internal sealed class PostgreSqlObjectScripter
{
    private readonly CatalogReader _catalog;
    private readonly PostgreSqlObjectsDiscovery _discovery;
    private readonly PostgreSqlTableScripter _tables;
    private readonly PostgreSqlTypeScripter _types;
    private readonly PostgreSqlSequenceScripter _sequences;
    private readonly ILogger _logger;

    public PostgreSqlObjectScripter(CatalogReader catalog, PostgreSqlObjectsDiscovery discovery, ILogger logger)
    {
        _catalog = catalog;
        _discovery = discovery;
        _tables = new PostgreSqlTableScripter(catalog);
        _types = new PostgreSqlTypeScripter(catalog);
        _sequences = new PostgreSqlSequenceScripter(catalog);
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> Script(PostgreSqlObject dbObject, CancellationToken cancellationToken)
    {
        var statement = await Build(dbObject, cancellationToken);

        if (string.IsNullOrWhiteSpace(statement))
        {
            _logger.LogWarning("Skipping {ObjectKey}: the server returned no definition", dbObject.Key);
            return [];
        }

        return [statement.TrimEnd().TrimEnd(';')];
    }

    private async Task<string?> Build(PostgreSqlObject dbObject, CancellationToken cancellationToken) => dbObject.Type switch
    {
        PostgreSqlObjectType.Schema => $"CREATE SCHEMA {dbObject.Schema.Quote()}",

        PostgreSqlObjectType.Type or PostgreSqlObjectType.Domain =>
            PostgreSqlTypeScripter.Render(await _types.Read(dbObject, cancellationToken)),

        PostgreSqlObjectType.Sequence => await BuildSequence(dbObject, cancellationToken),

        PostgreSqlObjectType.Function or PostgreSqlObjectType.Procedure =>
            await Definition(PostgreSqlDdlQueries.FunctionDefinition, dbObject, cancellationToken),

        PostgreSqlObjectType.Aggregate => await BuildAggregate(dbObject, cancellationToken),

        PostgreSqlObjectType.Table => PostgreSqlTableScripter.Render(
            await _tables.Read(dbObject, _discovery.OptionsOf(dbObject.Oid), _discovery.InheritedBy(dbObject),
                cancellationToken)),

        PostgreSqlObjectType.Partition =>
            $"ALTER TABLE ONLY {dbObject.QualifiedParent} ATTACH PARTITION {dbObject.QualifiedName} {dbObject.Detail}",

        PostgreSqlObjectType.SequenceOwner =>
            $"ALTER SEQUENCE {dbObject.QualifiedName} OWNED BY {dbObject.QualifiedParent}.{dbObject.Detail!.Quote()}",

        PostgreSqlObjectType.View => await BuildView(dbObject, "VIEW", string.Empty, cancellationToken),

        // A schema script creates the view, not its contents; a REFRESH is the deployer's business.
        PostgreSqlObjectType.MaterializedView =>
            await BuildView(dbObject, "MATERIALIZED VIEW", Environment.NewLine + "  WITH NO DATA", cancellationToken),

        PostgreSqlObjectType.Constraint or PostgreSqlObjectType.ForeignKey =>
            await BuildConstraint(dbObject, cancellationToken),

        PostgreSqlObjectType.Index => await Definition(PostgreSqlDdlQueries.IndexDefinition, dbObject, cancellationToken),

        PostgreSqlObjectType.Trigger => await Definition(PostgreSqlDdlQueries.TriggerDefinition, dbObject, cancellationToken),

        PostgreSqlObjectType.Rule => await Definition(PostgreSqlDdlQueries.RuleDefinition, dbObject, cancellationToken),

        _ => throw new NotSupportedException($"{dbObject.Type} is not an object type this tool can script"),
    };

    private async Task<string?> BuildSequence(PostgreSqlObject dbObject, CancellationToken cancellationToken)
    {
        var sequence = await _sequences.Read(dbObject.Oid, cancellationToken);

        return sequence is null
            ? $"CREATE SEQUENCE {dbObject.QualifiedName}"
            : PostgreSqlSequenceScripter.Render(sequence, dbObject.Schema, dbObject.Name);
    }

    private async Task<string?> BuildView(
        PostgreSqlObject dbObject,
        string keyword,
        string suffix,
        CancellationToken cancellationToken)
    {
        var definition = await Definition(PostgreSqlDdlQueries.ViewDefinition, dbObject, cancellationToken);
        if (string.IsNullOrWhiteSpace(definition))
            return null;

        // WITH CHECK OPTION and security_barrier both live in reloptions, which pg_get_viewdef
        // does not carry, so one clause covers them both.
        var options = _discovery.OptionsOf(dbObject.Oid).StorageOptions;
        var with = options.Length > 0 ? $" WITH ({options})" : string.Empty;

        return $"CREATE {keyword} {dbObject.QualifiedName}{with} AS{Environment.NewLine}" +
               definition.TrimEnd().TrimEnd(';') + suffix;
    }

    private async Task<string?> BuildConstraint(PostgreSqlObject dbObject, CancellationToken cancellationToken)
    {
        var definition = await Definition(PostgreSqlDdlQueries.ConstraintDefinition, dbObject, cancellationToken);

        return string.IsNullOrWhiteSpace(definition)
            ? null
            : $"ALTER TABLE ONLY {dbObject.QualifiedParent} ADD CONSTRAINT {dbObject.Name.Quote()} {definition}";
    }

    /// <summary>
    /// <c>pg_get_functiondef</c> raises an error on an aggregate rather than returning anything, so
    /// this is the one routine kind that has to be put back together by hand.
    /// </summary>
    private async Task<string?> BuildAggregate(PostgreSqlObject dbObject, CancellationToken cancellationToken)
    {
        var rows = await _catalog.Query(
            PostgreSqlDdlQueries.Aggregate,
            $"aggregate {dbObject.Key}",
            reader => (
                StateType: reader.GetString(0),
                StateFunction: reader.GetString(1),
                FinalFunction: reader.GetString(2),
                CombineFunction: reader.GetString(3),
                InitialCondition: reader.GetString(4),
                HasInitialCondition: reader.GetBoolean(5),
                SortOperator: reader.GetString(6)),
            cancellationToken,
            ("oid", (long)dbObject.Oid));

        if (rows.Count == 0)
            return null;

        var aggregate = rows[0];

        var options = new List<string>
        {
            "SFUNC = " + Unqualify(aggregate.StateFunction),
            "STYPE = " + aggregate.StateType,
        };

        if (aggregate.FinalFunction.Length > 0)
            options.Add("FINALFUNC = " + Unqualify(aggregate.FinalFunction));

        if (aggregate.CombineFunction.Length > 0)
            options.Add("COMBINEFUNC = " + Unqualify(aggregate.CombineFunction));

        if (aggregate.HasInitialCondition)
            options.Add("INITCOND = " + aggregate.InitialCondition.ToSqlLiteral());

        if (aggregate.SortOperator.Length > 0)
            options.Add("SORTOP = " + Unqualify(aggregate.SortOperator));

        return $"CREATE AGGREGATE {dbObject.QualifiedName}({dbObject.Arguments}) ({Environment.NewLine}    " +
               string.Join("," + Environment.NewLine + "    ", options) + Environment.NewLine + ")";
    }

    /// <summary>
    /// A <c>regprocedure</c> renders as <c>schema.name(argtypes)</c>; the option only wants the
    /// function, and the argument list is implied by the aggregate's own.
    /// </summary>
    private static string Unqualify(string regProcedure)
    {
        var parenthesis = regProcedure.IndexOf('(', StringComparison.Ordinal);
        return parenthesis < 0 ? regProcedure : regProcedure[..parenthesis];
    }

    private async Task<string?> Definition(string sql, PostgreSqlObject dbObject, CancellationToken cancellationToken)
    {
        var definitions = await _catalog.Query(
            sql,
            $"definition of {dbObject.Key}",
            reader => reader.IsDBNull(0) ? null : reader.GetString(0),
            cancellationToken,
            ("oid", (long)dbObject.Oid));

        return definitions.FirstOrDefault();
    }
}
