using System.Globalization;
using System.Text;
using Grillisoft.Tools.DatabaseDeploy.Database;

namespace Grillisoft.Tools.DatabaseDeploy.PostgreSql.Ddl;

/// <summary>
/// The options of a sequence, whether it stands on its own or backs an identity column.
/// </summary>
internal sealed record PostgreSqlSequenceDefinition(
    string Type,
    long Start,
    long Increment,
    long Minimum,
    long Maximum,
    long Cache,
    bool Cycle);

/// <summary>
/// Builds <c>CREATE SEQUENCE</c>, and the option list an identity column carries.
/// </summary>
/// <remarks>
/// Reading and rendering are separate so the rendering - which is where all the decisions are - can
/// be tested without a server. No <c>setval</c> is ever written: where a sequence has got to is
/// data, and this is a schema script.
/// </remarks>
internal sealed class PostgreSqlSequenceScripter(CatalogReader catalog)
{
    /// <summary>
    /// The defaults PostgreSQL uses. An option equal to its default is left out, so the statement
    /// says only what is actually true of this sequence.
    /// </summary>
    private const long DefaultIncrement = 1;
    private const long DefaultCache = 1;

    public async Task<PostgreSqlSequenceDefinition?> Read(uint oid, CancellationToken cancellationToken)
    {
        var rows = await catalog.Query(
            PostgreSqlDdlQueries.Sequence,
            $"sequence {oid}",
            reader => new PostgreSqlSequenceDefinition(
                reader.GetString(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.GetInt64(3),
                reader.GetInt64(4),
                reader.GetInt64(5),
                reader.GetBoolean(6)),
            cancellationToken,
            ("oid", (long)oid));

        return rows.FirstOrDefault();
    }

    public static string Render(PostgreSqlSequenceDefinition sequence, string schema, string name)
    {
        var sql = new StringBuilder("CREATE SEQUENCE ").Append(name.Qualify(schema));

        if (!string.Equals(sequence.Type, "bigint", StringComparison.Ordinal))
            sql.Append(Environment.NewLine).Append("    AS ").Append(sequence.Type);

        sql.Append(Environment.NewLine).Append("    START WITH ").Append(Format(sequence.Start));
        sql.Append(Environment.NewLine).Append("    INCREMENT BY ").Append(Format(sequence.Increment));

        Append(sql, "MINVALUE", sequence.Minimum, IsDefaultMinimum(sequence), "NO MINVALUE");
        Append(sql, "MAXVALUE", sequence.Maximum, IsDefaultMaximum(sequence), "NO MAXVALUE");

        if (sequence.Cache != DefaultCache)
            sql.Append(Environment.NewLine).Append("    CACHE ").Append(Format(sequence.Cache));

        if (sequence.Cycle)
            sql.Append(Environment.NewLine).Append("    CYCLE");

        return sql.ToString();
    }

    /// <summary>
    /// The options of an identity column, written inside its <c>GENERATED ... AS IDENTITY</c>
    /// clause. Empty when the sequence is entirely default, which is the common case.
    /// </summary>
    public static string RenderIdentityOptions(PostgreSqlSequenceDefinition sequence)
    {
        var options = new List<string>();

        if (sequence.Increment != DefaultIncrement)
            options.Add("INCREMENT BY " + Format(sequence.Increment));

        if (!IsDefaultMinimum(sequence))
            options.Add("MINVALUE " + Format(sequence.Minimum));

        if (!IsDefaultMaximum(sequence))
            options.Add("MAXVALUE " + Format(sequence.Maximum));

        if (sequence.Start != (sequence.Increment > 0 ? sequence.Minimum : sequence.Maximum))
            options.Add("START WITH " + Format(sequence.Start));

        if (sequence.Cache != DefaultCache)
            options.Add("CACHE " + Format(sequence.Cache));

        if (sequence.Cycle)
            options.Add("CYCLE");

        return options.Count == 0 ? string.Empty : " ( " + string.Join(' ', options) + " )";
    }

    private static void Append(StringBuilder sql, string keyword, long value, bool isDefault, string none)
    {
        sql.Append(Environment.NewLine).Append("    ");

        if (isDefault)
            sql.Append(none);
        else
            sql.Append(keyword).Append(' ').Append(Format(value));
    }

    /// <summary>
    /// An ascending sequence's minimum defaults to 1 and a descending one's to the smallest value
    /// its type can hold; the reverse for the maximum. Only a bound that is not the default is
    /// worth writing.
    /// </summary>
    private static bool IsDefaultMinimum(PostgreSqlSequenceDefinition sequence)
        => sequence.Increment > 0 ? sequence.Minimum == 1 : sequence.Minimum == MinimumOf(sequence.Type);

    private static bool IsDefaultMaximum(PostgreSqlSequenceDefinition sequence)
        => sequence.Increment > 0 ? sequence.Maximum == MaximumOf(sequence.Type) : sequence.Maximum == -1;

    private static long MinimumOf(string type) => type switch
    {
        "smallint" => short.MinValue,
        "integer" => int.MinValue,
        _ => long.MinValue,
    };

    private static long MaximumOf(string type) => type switch
    {
        "smallint" => short.MaxValue,
        "integer" => int.MaxValue,
        _ => long.MaxValue,
    };

    private static string Format(long value) => value.ToString(CultureInfo.InvariantCulture);
}
