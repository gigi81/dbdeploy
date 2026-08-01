using System.Collections.Frozen;
using Grillisoft.Tools.DatabaseDeploy.Database.Formatting;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer.Formatting;

/// <summary>
/// T-SQL: bracket-quoted identifiers, <c>N'…'</c> literals, and <c>GO</c> as the batch separator.
/// </summary>
internal sealed class SqlServerDialect : SqlDialect
{
    public override string Name => SqlServerDatabaseFactory.ProviderName;

    private static readonly string[] ExtraStatement =
    [
        "BACKUP", "RESTORE", "PRINT", "RAISERROR", "THROW", "WAITFOR", "GOTO", "BREAK", "CONTINUE",
        "DISABLE", "ENABLE"
    ];

    private static readonly string[] ExtraContinuation = ["CROSS APPLY", "OUTER APPLY"];

    private static readonly string[] ExtraClause = ["OUTPUT", "PIVOT", "UNPIVOT"];

    private static readonly string[] ExtraReserved =
    [
        "AFTER", "APPLY", "AUTHORIZATION", "CLUSTERED", "NONCLUSTERED", "COMPUTED", "DATABASE",
        "DELETED", "GO", "INCLUDE", "INSERTED", "INSTEAD", "NOCHECK", "NOCOUNT", "OFF", "ON",
        "PERSISTED", "READONLY", "RETURNS", "ROWGUIDCOL", "SPARSE", "STATISTICS", "TOP", "TRY",
        "CATCH", "WITH"
    ];

    /// <summary>
    /// <c>IDENTITY</c> belongs here rather than with the keywords so that it closes up against its
    /// argument list, the way <c>IDENTITY(1, 1)</c> is always written.
    /// </summary>
    private static readonly string[] ExtraFunctions =
    [
        "CHARINDEX", "DATEADD", "DATEDIFF", "DATENAME", "DATEPART", "DAY", "GETDATE", "GETUTCDATE",
        "IDENTITY", "IIF", "ISNULL", "LEN", "MONTH", "NEWID", "PATINDEX", "SCOPE_IDENTITY",
        "STUFF", "SYSDATETIME", "TRY_CAST", "TRY_CONVERT", "YEAR"
    ];

    private static readonly string[] ExtraDataTypes =
    [
        "DATETIME2", "DATETIMEOFFSET", "GEOGRAPHY", "GEOMETRY", "HIERARCHYID", "IMAGE", "NTEXT",
        "SMALLDATETIME", "SMALLMONEY", "SQL_VARIANT", "UNIQUEIDENTIFIER"
    ];

    public override FrozenSet<string> StatementKeywords { get; } =
        SqlKeywords.Set(SqlKeywords.Statement, ExtraStatement);

    public override FrozenSet<string> ClauseKeywords { get; } =
        SqlKeywords.Set(SqlKeywords.Clause, ExtraClause);

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
        ExtraClause,
        ExtraContinuation,
        ExtraReserved);

    public override FrozenSet<string> DataTypes { get; } =
        SqlKeywords.Set(SqlKeywords.DataTypes, ExtraDataTypes);

    public override FrozenSet<string> Functions { get; } =
        SqlKeywords.Set(SqlKeywords.Functions, ExtraFunctions);

    /// <summary>T-SQL writes <c>IF … BEGIN … END</c>, with no <c>THEN</c> and no <c>END IF</c>.</summary>
    public override bool UsesThenForIf => false;

    public override char[] IdentifierQuotes => ['[', '"'];

    public override string BatchSeparator => "GO";

    /// <summary>
    /// Recognises the Unicode and binary literal forms the shared lexer would otherwise split into
    /// a word followed by a string.
    /// </summary>
    public override bool TryReadSpecial(ReadOnlySpan<char> input, out SqlTokenKind kind, out int length)
    {
        kind = default;
        length = 0;

        if (input.Length >= 2 && (input[0] is 'N' or 'n') && input[1] == '\'')
        {
            length = 1 + QuotedLength(input[1..]);
            kind = SqlTokenKind.StringLiteral;
            return true;
        }

        if (input.Length >= 3 && input[0] == '0' && (input[1] is 'x' or 'X') && char.IsAsciiHexDigit(input[2]))
        {
            var i = 2;
            while (i < input.Length && char.IsAsciiHexDigit(input[i]))
                i++;

            length = i;
            kind = SqlTokenKind.Number;
            return true;
        }

        return false;
    }

    /// <summary>
    /// The length of the literal starting at the opening quote, treating a doubled quote as an
    /// escape.
    /// </summary>
    private static int QuotedLength(ReadOnlySpan<char> input)
    {
        var i = 1;

        while (i < input.Length)
        {
            if (input[i] != '\'')
            {
                i++;
                continue;
            }

            if (i + 1 >= input.Length || input[i + 1] != '\'')
                return i + 1;

            i += 2;
        }

        return input.Length;
    }
}
