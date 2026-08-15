using System.Text;
using Grillisoft.Tools.DatabaseDeploy.Database;

namespace Grillisoft.Tools.DatabaseDeploy.PostgreSql.Ddl;

/// <summary>
/// Builds a <c>CREATE TABLE</c> out of <c>pg_catalog</c>.
/// </summary>
/// <remarks>
/// This is the one statement PostgreSQL has no function to produce: <c>pg_get_viewdef</c> and its
/// siblings cover everything else, but a table has to be assembled a column at a time. Reading and
/// rendering are separate methods so that the rendering - which is where every decision is - can be
/// tested without a server.
/// <para>
/// No constraint is ever written inline. They all come out as <c>ALTER TABLE ... ADD CONSTRAINT</c>
/// objects of their own, which is what <c>pg_dump</c> does and what lets two tables reference each
/// other; the same reasoning as the foreign keys on MySQL.
/// </para>
/// </remarks>
internal sealed class PostgreSqlTableScripter(CatalogReader catalog)
{
    /// <param name="options">
    /// The parts of <c>pg_class</c> the discovery already read, rather than a second trip for them.
    /// </param>
    /// <param name="inherits">The qualified names of the tables this one inherits, in order.</param>
    public async Task<PostgreSqlTableDefinition> Read(
        PostgreSqlObject table,
        PostgreSqlRelationOptions options,
        IReadOnlyList<string> inherits,
        CancellationToken cancellationToken)
    {
        var identitySequences = await ReadIdentitySequences(table.Oid, cancellationToken);

        var columns = await catalog.Query(
            PostgreSqlDdlQueries.Columns,
            $"columns of {table.Key}",
            reader =>
            {
                var name = reader.GetString(0);
                return new PostgreSqlColumn(
                    name,
                    reader.GetString(1),
                    reader.GetBoolean(2),
                    ReadChar(reader.GetValue(3)),
                    ReadChar(reader.GetValue(4)),
                    reader.GetBoolean(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    identitySequences.GetValueOrDefault(name));
            },
            cancellationToken,
            ("oid", (long)table.Oid));

        return new PostgreSqlTableDefinition(
            table.Schema,
            table.Name,
            columns,
            Unlogged: options.Persistence == 'u',
            options.PartitionKey,
            options.StorageOptions,
            inherits,
            options.IsPartition);
    }

    private async Task<Dictionary<string, PostgreSqlSequenceDefinition>> ReadIdentitySequences(
        uint oid,
        CancellationToken cancellationToken)
    {
        var sequences = await catalog.TryQuery(
            PostgreSqlDdlQueries.IdentitySequences,
            $"identity sequences of relation {oid}",
            reader => (
                Column: reader.GetString(0),
                Sequence: new PostgreSqlSequenceDefinition(
                    reader.GetString(1),
                    reader.GetInt64(2),
                    reader.GetInt64(3),
                    reader.GetInt64(4),
                    reader.GetInt64(5),
                    reader.GetInt64(6),
                    reader.GetBoolean(7))),
            cancellationToken,
            ("oid", (long)oid));

        return sequences.ToDictionary(s => s.Column, s => s.Sequence, StringComparer.Ordinal);
    }

    public static string Render(PostgreSqlTableDefinition table)
    {
        var sql = new StringBuilder("CREATE ");

        if (table.Unlogged)
            sql.Append("UNLOGGED ");

        sql.Append("TABLE ").Append(table.Name.Qualify(table.Schema)).Append(" (");

        // A column the table only inherits is created by the INHERITS clause; repeating it here
        // would redeclare it. A partition is the exception: its columns all look inherited, but it
        // is created on its own and attached afterwards, so it has to declare every one of them.
        var columns = table.Columns.Where(column => column.IsLocal || table.IsPartition).ToList();

        if (columns.Count > 0)
        {
            sql.Append(Environment.NewLine);
            sql.AppendJoin("," + Environment.NewLine, columns.Select(column => "    " + RenderColumn(column)));
            sql.Append(Environment.NewLine);
        }

        sql.Append(')');

        if (table.Inherits.Count > 0)
            sql.Append(Environment.NewLine).Append("INHERITS (").AppendJoin(", ", table.Inherits).Append(')');

        if (table.PartitionKey.Length > 0)
            sql.Append(Environment.NewLine).Append("PARTITION BY ").Append(table.PartitionKey);

        if (table.StorageOptions.Length > 0)
            sql.Append(Environment.NewLine).Append("WITH (").Append(table.StorageOptions).Append(')');

        return sql.ToString();
    }

    private static string RenderColumn(PostgreSqlColumn column)
    {
        var sql = new StringBuilder(column.Name.Quote()).Append(' ').Append(column.Type);

        // Only a collation the type does not already imply is worth writing; the read leaves the
        // name empty otherwise.
        if (column.CollationName.Length > 0)
            sql.Append(" COLLATE ").Append(column.CollationName.Qualify(column.CollationSchema));

        if (column.Generated == 's')
        {
            // The generation expression lives where a default would, and the two are exclusive.
            sql.Append(" GENERATED ALWAYS AS (").Append(column.Default).Append(") STORED");
        }
        else if (column.Identity is 'a' or 'd')
        {
            sql.Append(" GENERATED ").Append(column.Identity == 'a' ? "ALWAYS" : "BY DEFAULT").Append(" AS IDENTITY");

            if (column.IdentitySequence is { } sequence)
                sql.Append(PostgreSqlSequenceScripter.RenderIdentityOptions(sequence));
        }
        else if (column.Default.Length > 0)
        {
            sql.Append(" DEFAULT ").Append(column.Default);
        }

        if (column.NotNull)
            sql.Append(" NOT NULL");

        return sql.ToString();
    }

    /// <summary>
    /// <c>attidentity</c> and <c>attgenerated</c> come back as a one character string on some
    /// drivers and as a char on others, and empty when the column is neither.
    /// </summary>
    private static char ReadChar(object value) => value switch
    {
        char c => c,
        string { Length: > 0 } s => s[0],
        _ => ' ',
    };
}
