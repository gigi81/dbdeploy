using System.Text;
using Grillisoft.Tools.DatabaseDeploy.Database;

namespace Grillisoft.Tools.DatabaseDeploy.PostgreSql.Ddl;

/// <summary>A user defined type, in whichever of its shapes.</summary>
/// <param name="Kind">The catalog's <c>typtype</c>: e for enum, c for composite, d for domain.</param>
internal sealed record PostgreSqlTypeDefinition(
    string Schema,
    string Name,
    char Kind,
    IReadOnlyList<string> Labels,
    IReadOnlyList<(string Name, string Type)> Attributes,
    string BaseType,
    bool NotNull,
    string Default,
    IReadOnlyList<(string Name, string Definition)> Constraints);

/// <summary>
/// Builds <c>CREATE TYPE</c> and <c>CREATE DOMAIN</c>, which PostgreSQL has no function to produce.
/// </summary>
/// <remarks>
/// Reading and rendering are separate so the rendering can be tested without a server. Range types
/// are read but not assembled: their definition names a subtype operator class, a canonical
/// function and a subtype diff function, and getting one of those wrong produces a type that is
/// quietly not the same. They fail as one object instead, which the script reports and the rest of
/// the generation survives.
/// </remarks>
internal sealed class PostgreSqlTypeScripter(CatalogReader catalog)
{
    public async Task<PostgreSqlTypeDefinition> Read(PostgreSqlObject type, CancellationToken cancellationToken)
    {
        var kind = type.Detail is { Length: > 0 } detail ? detail[0] : 'e';

        return kind switch
        {
            'e' => new PostgreSqlTypeDefinition(
                type.Schema, type.Name, kind,
                await ReadLabels(type, cancellationToken),
                [], string.Empty, false, string.Empty, []),

            'c' => new PostgreSqlTypeDefinition(
                type.Schema, type.Name, kind,
                [], await ReadAttributes(type, cancellationToken),
                string.Empty, false, string.Empty, []),

            'd' => await ReadDomain(type, cancellationToken),

            _ => new PostgreSqlTypeDefinition(
                type.Schema, type.Name, kind, [], [], string.Empty, false, string.Empty, []),
        };
    }

    private async Task<List<string>> ReadLabels(PostgreSqlObject type, CancellationToken cancellationToken)
        => await catalog.Query(
            PostgreSqlDdlQueries.EnumLabels,
            $"labels of {type.Key}",
            reader => reader.GetString(0),
            cancellationToken,
            ("oid", (long)type.Oid));

    private async Task<List<(string Name, string Type)>> ReadAttributes(
        PostgreSqlObject type,
        CancellationToken cancellationToken)
        => await catalog.Query(
            PostgreSqlDdlQueries.CompositeAttributes,
            $"attributes of {type.Key}",
            reader => (reader.GetString(0), reader.GetString(1)),
            cancellationToken,
            ("oid", (long)type.Oid));

    private async Task<PostgreSqlTypeDefinition> ReadDomain(
        PostgreSqlObject type,
        CancellationToken cancellationToken)
    {
        var rows = await catalog.Query(
            PostgreSqlDdlQueries.Domain,
            $"domain {type.Key}",
            reader => (Base: reader.GetString(0), NotNull: reader.GetBoolean(1), Default: reader.GetString(2)),
            cancellationToken,
            ("oid", (long)type.Oid));

        var constraints = await catalog.TryQuery(
            PostgreSqlDdlQueries.DomainConstraints,
            $"constraints of {type.Key}",
            reader => (Definition: reader.GetString(0), Name: reader.GetString(1)),
            cancellationToken,
            ("oid", (long)type.Oid));

        var domain = rows.FirstOrDefault();

        return new PostgreSqlTypeDefinition(
            type.Schema, type.Name, 'd', [], [],
            domain.Base ?? string.Empty,
            domain.NotNull,
            domain.Default ?? string.Empty,
            constraints.Select(c => (c.Name, c.Definition)).ToList());
    }

    public static string Render(PostgreSqlTypeDefinition type)
    {
        var name = type.Name.Qualify(type.Schema);

        return type.Kind switch
        {
            'e' => RenderEnum(type, name),
            'c' => RenderComposite(type, name),
            'd' => RenderDomain(type, name),
            _ => throw new NotSupportedException(
                $"type {name} is a {type.Kind} type, which this tool cannot script"),
        };
    }

    private static string RenderEnum(PostgreSqlTypeDefinition type, string name)
    {
        var sql = new StringBuilder("CREATE TYPE ").Append(name).Append(" AS ENUM (");

        if (type.Labels.Count > 0)
        {
            sql.Append(Environment.NewLine);
            sql.AppendJoin("," + Environment.NewLine, type.Labels.Select(label => "    " + label.ToSqlLiteral()));
            sql.Append(Environment.NewLine);
        }

        return sql.Append(')').ToString();
    }

    private static string RenderComposite(PostgreSqlTypeDefinition type, string name)
    {
        var sql = new StringBuilder("CREATE TYPE ").Append(name).Append(" AS (");

        if (type.Attributes.Count > 0)
        {
            sql.Append(Environment.NewLine);
            sql.AppendJoin("," + Environment.NewLine,
                type.Attributes.Select(a => $"    {a.Name.Quote()} {a.Type}"));
            sql.Append(Environment.NewLine);
        }

        return sql.Append(')').ToString();
    }

    private static string RenderDomain(PostgreSqlTypeDefinition type, string name)
    {
        var sql = new StringBuilder("CREATE DOMAIN ").Append(name).Append(" AS ").Append(type.BaseType);

        if (type.Default.Length > 0)
            sql.Append(Environment.NewLine).Append("    DEFAULT ").Append(type.Default);

        if (type.NotNull)
            sql.Append(Environment.NewLine).Append("    NOT NULL");

        foreach (var (constraintName, definition) in type.Constraints)
        {
            sql.Append(Environment.NewLine).Append("    CONSTRAINT ")
               .Append(constraintName.Quote()).Append(' ').Append(definition);
        }

        return sql.ToString();
    }
}
