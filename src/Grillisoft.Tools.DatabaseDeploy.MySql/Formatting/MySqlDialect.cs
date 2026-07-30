using System.Collections.Frozen;
using Grillisoft.Tools.DatabaseDeploy.Database.Formatting;

namespace Grillisoft.Tools.DatabaseDeploy.MySql.Formatting;

/// <summary>
/// MySQL and MariaDB: backtick identifiers, <c>#</c> comments, backslash escapes, and the
/// <c>DELIMITER</c> statement that lets a routine body contain semicolons.
/// </summary>
internal sealed class MySqlDialect : SqlDialect
{
    private static readonly string[] ExtraStatement = ["DELIMITER", "REPLACE INTO", "LOCK", "UNLOCK", "FLUSH"];

    private static readonly string[] ExtraContinuation = ["STRAIGHT_JOIN"];

    private static readonly string[] ExtraReserved =
    [
        "ALGORITHM", "AUTO_INCREMENT", "CHARSET", "COLLATION", "COMMENT", "CONTAINS", "DATA", "DEFINER",
        "DELAYED", "DETERMINISTIC", "DUPLICATE", "ENGINE", "ELSEIF", "IGNORE", "INVOKER",
        "ITERATE", "LEAVE", "MODIFIES", "READS", "REPEAT", "RETURNS", "SEPARATOR", "SIGNAL",
        "SQL", "STRAIGHT_JOIN", "TEMPORARY", "UNSIGNED", "UNTIL", "ZEROFILL"
    ];

    private static readonly string[] ExtraDataTypes =
    [
        "DATETIME", "ENUM", "LONGBLOB", "LONGTEXT", "MEDIUMBLOB", "MEDIUMINT", "MEDIUMTEXT",
        "TINYBLOB", "TINYTEXT", "YEAR"
    ];

    public override FrozenSet<string> StatementKeywords { get; } =
        SqlKeywords.Set(SqlKeywords.Statement, ExtraStatement);

    public override FrozenSet<string> ClauseKeywords { get; } = SqlKeywords.Set(SqlKeywords.Clause);

    public override FrozenDictionary<string, string> ContextualClauseKeywords { get; } =
        SqlKeywords.ContextualClause.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public override FrozenSet<string> ContinuationKeywords { get; } =
        SqlKeywords.Set(SqlKeywords.Continuation, ExtraContinuation);

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
        ExtraContinuation,
        ExtraReserved);

    public override FrozenSet<string> DataTypes { get; } =
        SqlKeywords.Set(SqlKeywords.DataTypes, ExtraDataTypes);

    public override bool UsesThenForIf => true;

    public override char[] IdentifierQuotes => ['`', '"'];

    public override bool SupportsHashLineComment => true;

    public override bool SupportsBackslashEscapes => true;

    /// <summary>
    /// A <c>DELIMITER</c> line is a client instruction rather than SQL, so it is reproduced as
    /// written.
    /// </summary>
    public override bool IsPassthroughLine(string trimmedLine) =>
        trimmedLine.StartsWith("DELIMITER ", StringComparison.OrdinalIgnoreCase);

    public override bool TryReadDelimiterChange(string trimmedLine, out string delimiter)
    {
        delimiter = string.Empty;

        if (!IsPassthroughLine(trimmedLine))
            return false;

        delimiter = trimmedLine["DELIMITER ".Length..].Trim();
        return delimiter.Length > 0;
    }
}
