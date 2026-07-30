using System.Collections.Frozen;
using Grillisoft.Tools.DatabaseDeploy.Database.Formatting;

namespace Grillisoft.Tools.DatabaseDeploy.PostgreSql.Formatting;

/// <summary>
/// PostgreSQL: PL/pgSQL blocks and dollar-quoted bodies, which have to be read as one literal so
/// that the semicolons inside a function are not mistaken for statement terminators.
/// </summary>
internal sealed class PostgreSqlDialect : SqlDialect
{
    private static readonly string[] ExtraStatement = ["DO", "COMMENT ON", "RAISE", "PERFORM", "REFRESH"];

    private static readonly string[] ExtraClause = ["ON CONFLICT"];

    private static readonly string[] ExtraReserved =
    [
        "COST", "DEFINER", "ELSIF", "EXCEPTION", "EXTENSION", "IMMUTABLE", "INVOKER", "LANGUAGE",
        "MATERIALIZED", "NEW", "NOTICE", "OLD", "OWNER", "PLPGSQL", "RECURSIVE", "RETURNS",
        "SECURITY", "SEQUENCE", "SETOF", "STABLE", "STRICT", "TEMPORARY", "UNLOGGED", "VARIADIC",
        "VOLATILE", "CONFLICT", "NOTHING"
    ];

    private static readonly string[] ExtraDataTypes =
    [
        "BIGSERIAL", "BOX", "BYTEA", "CIDR", "CIRCLE", "DATERANGE", "INET", "INT2", "INT4", "INT8",
        "JSON", "JSONB", "LINE", "LSEG", "MACADDR", "MONEY", "PATH", "POINT", "POLYGON", "SERIAL",
        "SMALLSERIAL", "TSQUERY", "TSVECTOR", "TIMESTAMPTZ", "TIMETZ"
    ];

    public override FrozenSet<string> StatementKeywords { get; } =
        SqlKeywords.Set(SqlKeywords.Statement, ExtraStatement);

    public override FrozenSet<string> ClauseKeywords { get; } =
        SqlKeywords.Set(SqlKeywords.Clause, ExtraClause);

    public override FrozenDictionary<string, string> ContextualClauseKeywords { get; } =
        SqlKeywords.ContextualClause.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public override FrozenSet<string> ContinuationKeywords { get; } =
        SqlKeywords.Set(SqlKeywords.Continuation);

    public override FrozenSet<string> SetOperatorKeywords { get; } =
        SqlKeywords.Set(SqlKeywords.SetOperator);

    public override FrozenSet<string> Reserved { get; } = SqlKeywords.Set(
        SqlKeywords.Reserved,
        SqlKeywords.Statement,
        SqlKeywords.Clause,
        SqlKeywords.Line,
        SqlKeywords.Continuation,
        SqlKeywords.SetOperator,
        SqlKeywords.BlockOpen,
        ExtraStatement,
        ExtraClause,
        ExtraReserved);

    public override FrozenSet<string> DataTypes { get; } =
        SqlKeywords.Set(SqlKeywords.DataTypes, ExtraDataTypes);

    public override bool UsesThenForIf => true;

    /// <summary>
    /// Reads a dollar-quoted body - <c>$$…$$</c> or <c>$tag$…$tag$</c> - as a single literal.
    /// </summary>
    public override bool TryReadSpecial(ReadOnlySpan<char> input, out SqlTokenKind kind, out int length)
    {
        kind = SqlTokenKind.StringLiteral;
        length = 0;

        if (input.Length < 2 || input[0] != '$')
            return false;

        var tagEnd = input[1..].IndexOf('$');
        if (tagEnd < 0)
            return false;

        var tag = input[..(tagEnd + 2)]; // includes both dollars

        for (var i = 1; i < tag.Length; i++)
        {
            if (!char.IsLetterOrDigit(tag[i]) && tag[i] is not ('_' or '$'))
                return false;
        }

        var close = input[tag.Length..].IndexOf(tag);
        if (close < 0)
            return false;

        length = tag.Length + close + tag.Length;
        return true;
    }
}
