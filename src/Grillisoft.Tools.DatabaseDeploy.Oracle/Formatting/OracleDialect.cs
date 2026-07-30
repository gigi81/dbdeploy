using System.Collections.Frozen;
using Grillisoft.Tools.DatabaseDeploy.Database.Formatting;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle.Formatting;

/// <summary>
/// PL/SQL: <c>BEGIN … END</c> blocks, <c>/</c> on a line of its own to end a statement, and the
/// SQL*Plus directives that surround the checked-in Oracle scripts.
/// </summary>
internal sealed class OracleDialect : SqlDialect
{
    /// <summary>
    /// SQL*Plus directives, which are not SQL and have to reach the server untouched.
    /// <c>SET</c> is deliberately absent - it needs the extra checks in
    /// <see cref="IsPassthroughLine"/> to tell <c>SET LINESIZE 80</c> from an UPDATE's SET clause.
    /// </summary>
    private static readonly string[] SqlPlusDirectives =
    [
        "REM", "PROMPT", "SPOOL", "DEFINE", "UNDEFINE", "WHENEVER", "ACCEPT", "COLUMN",
        "TTITLE", "BTITLE", "CONNECT", "DISCONNECT", "START", "EXIT", "QUIT", "SHOW", "VARIABLE"
    ];

    private static readonly string[] ExtraStatement =
    [
        "COMMENT ON", "EXECUTE IMMEDIATE", "RAISE", "FETCH", "EXIT", "GOTO", "NULL", "PRAGMA"
    ];

    private static readonly string[] ExtraReserved =
    [
        "AUTHID", "BODY", "BULK", "COLLECT", "CONSTANT", "CURRENT_USER", "CURRVAL", "DEFINER",
        "DETERMINISTIC", "EACH", "ELSIF", "EXCEPTION", "EXECUTE", "FORALL", "IMMEDIATE", "INDEX",
        "INTO", "MATERIALIZED", "NEXTVAL", "NOCOPY", "OLD", "ORGANIZATION", "OTHERS", "OUT",
        "PACKAGE", "PARALLEL", "PIPELINED", "REPLACE", "RESULT_CACHE", "RETURN", "REVERSE",
        "ROWTYPE", "SEQUENCE", "SYNONYM", "TABLESPACE", "TYPE", "VARRAY", "NEW", "REFERENCING",
        "BEFORE", "AFTER", "DISABLE", "ENABLE"
    ];

    private static readonly string[] ExtraSetOperator = ["MINUS"];

    private static readonly string[] ExtraDataTypes =
    [
        "BFILE", "BINARY_DOUBLE", "BINARY_FLOAT", "BINARY_INTEGER", "LONG", "NCLOB", "NUMBER",
        "NVARCHAR2", "PLS_INTEGER", "RAW", "ROWID", "SIMPLE_INTEGER", "UROWID", "VARCHAR2",
        "XMLTYPE"
    ];

    public override FrozenSet<string> StatementKeywords { get; } =
        SqlKeywords.Set(SqlKeywords.Statement, ExtraStatement);

    public override FrozenSet<string> ClauseKeywords { get; } = SqlKeywords.Set(SqlKeywords.Clause);

    public override FrozenDictionary<string, string> ContextualClauseKeywords { get; } =
        SqlKeywords.ContextualClause.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public override FrozenSet<string> ContinuationKeywords { get; } =
        SqlKeywords.Set(SqlKeywords.Continuation);

    public override FrozenSet<string> SetOperatorKeywords { get; } =
        SqlKeywords.Set(SqlKeywords.SetOperator, ExtraSetOperator);

    public override FrozenSet<string> Reserved { get; } = SqlKeywords.Set(
        SqlKeywords.Reserved,
        SqlKeywords.Statement,
        SqlKeywords.Clause,
        SqlKeywords.Line,
        SqlKeywords.Continuation,
        SqlKeywords.SetOperator,
        SqlKeywords.BlockOpen,
        ExtraStatement,
        ExtraReserved,
        ExtraSetOperator);

    public override FrozenSet<string> DataTypes { get; } =
        SqlKeywords.Set(SqlKeywords.DataTypes, ExtraDataTypes);

    public override bool UsesThenForIf => true;

    public override string BatchSeparator => "/";

    /// <summary>
    /// Oracle has no <c>%</c> operator - it only ever introduces an attribute such as
    /// <c>%TYPE</c>, and spacing it out would not compile.
    /// </summary>
    public override bool IsTightOperator(string op) => op is "." or "::" or "%";

    public override bool IsPassthroughLine(string trimmedLine)
    {
        if (trimmedLine.Length == 0)
            return false;

        if (trimmedLine[0] == '@')
            return true;

        var space = trimmedLine.IndexOf(' ');
        var first = space < 0 ? trimmedLine : trimmedLine[..space];

        if (Array.Exists(SqlPlusDirectives, d => d.Equals(first, StringComparison.OrdinalIgnoreCase)))
            return true;

        return IsSqlPlusSet(trimmedLine, first);
    }

    /// <summary>
    /// <c>SET LINESIZE 80</c> is a SQL*Plus directive; <c>SET salary = 1</c> is the SET clause of an
    /// UPDATE, and a bare <c>SET</c> is one this formatter has already put on a line of its own.
    /// The assignment and the word count are what separate them.
    /// </summary>
    private static bool IsSqlPlusSet(string line, string first)
    {
        if (!first.Equals("SET", StringComparison.OrdinalIgnoreCase))
            return false;

        return !line.Contains('=', StringComparison.Ordinal)
               && !line.EndsWith(';')
               && line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2;
    }

    /// <summary>Reads the <c>q'[…]'</c> family and the national character literals.</summary>
    public override bool TryReadSpecial(ReadOnlySpan<char> input, out SqlTokenKind kind, out int length)
    {
        kind = SqlTokenKind.StringLiteral;
        length = 0;

        if (input.Length >= 4 && (input[0] is 'q' or 'Q') && input[1] == '\'')
            return TryReadAlternativeQuoted(input, 2, ref length);

        if (input.Length >= 5 && (input[0] is 'n' or 'N') && (input[1] is 'q' or 'Q') && input[2] == '\'')
            return TryReadAlternativeQuoted(input, 3, ref length);

        return false;
    }

    private static bool TryReadAlternativeQuoted(ReadOnlySpan<char> input, int start, ref int length)
    {
        var open = input[start];
        var close = open switch
        {
            '[' => ']',
            '{' => '}',
            '(' => ')',
            '<' => '>',
            _ => open
        };

        for (var i = start + 1; i < input.Length - 1; i++)
        {
            if (input[i] == close && input[i + 1] == '\'')
            {
                length = i + 2;
                return true;
            }
        }

        return false;
    }
}
