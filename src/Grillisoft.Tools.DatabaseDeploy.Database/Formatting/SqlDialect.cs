using System.Collections.Frozen;

namespace Grillisoft.Tools.DatabaseDeploy.Database.Formatting;

/// <summary>
/// Everything the lexer and the emitter need to know about one SQL dialect. Providers subclass this
/// the same way they supply their own <see cref="Abstractions.IScriptParser"/>.
/// </summary>
public abstract class SqlDialect
{
    /// <summary>
    /// The provider name this dialect belongs to, so a formatted file can say which dialect it was
    /// laid out with.
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// Words that start a statement and keep what follows on the same line, so that
    /// <c>CREATE TABLE dbo.Customers</c> stays whole.
    /// </summary>
    public abstract FrozenSet<string> StatementKeywords { get; }

    /// <summary>
    /// Words that go on a line of their own with their body indented one level, the way
    /// <c>SELECT</c> and <c>WHERE</c> do.
    /// </summary>
    public abstract FrozenSet<string> ClauseKeywords { get; }

    /// <summary>
    /// Clause keywords that only behave as clauses inside one kind of statement. <c>SET</c> is a
    /// clause in an <c>UPDATE</c> but a statement of its own in <c>SET ANSI_NULLS ON</c>.
    /// Keyed by the keyword, valued by the statement keyword that has to be in force.
    /// </summary>
    public virtual FrozenDictionary<string, string> ContextualClauseKeywords =>
        FrozenDictionary<string, string>.Empty;

    /// <summary>
    /// Words that start a new line at the current clause body indent and keep their operands
    /// inline: <c>AND</c>, <c>OR</c> and the join forms.
    /// </summary>
    public abstract FrozenSet<string> ContinuationKeywords { get; }

    /// <summary><c>UNION</c> and friends: alone on their line, back at the statement indent.</summary>
    public abstract FrozenSet<string> SetOperatorKeywords { get; }

    /// <summary>
    /// Words that start a new line but keep their value beside them, because the value is a single
    /// short thing that reads worse on a line of its own: <c>LIMIT 1</c>, not <c>LIMIT</c> then
    /// <c>1</c>.
    /// </summary>
    public virtual FrozenSet<string> LineKeywords { get; } = SqlKeywords.Set(SqlKeywords.Line);

    /// <summary>Every word that gets the keyword casing applied to it.</summary>
    public abstract FrozenSet<string> Reserved { get; }

    /// <summary>Every word that gets the data type casing applied to it.</summary>
    public abstract FrozenSet<string> DataTypes { get; }

    /// <summary>
    /// Built-in function names. A call to anything outside this set is the author's own routine and
    /// keeps whatever casing they gave it.
    /// </summary>
    public virtual FrozenSet<string> Functions { get; } = SqlKeywords.Set(SqlKeywords.Functions);

    /// <summary>
    /// True when <c>IF</c> opens a block terminated by <c>END IF</c>, as in PL/SQL. T-SQL uses a
    /// bare <c>BEGIN</c>/<c>END</c> instead, so its <c>IF</c> must not open a block of its own.
    /// </summary>
    public virtual bool UsesThenForIf => false;

    /// <summary>Characters that open a delimited identifier.</summary>
    public virtual char[] IdentifierQuotes => ['"'];

    /// <summary>The character that closes the identifier opened by <paramref name="open"/>.</summary>
    public virtual char ClosingQuote(char open) => open == '[' ? ']' : open;

    /// <summary>MySQL treats <c>#</c> as a line comment; nobody else does.</summary>
    public virtual bool SupportsHashLineComment => false;

    /// <summary>
    /// Operators written with no space on either side. Member access always qualifies; Oracle adds
    /// <c>%</c> because <c>%TYPE</c> is an attribute reference rather than an arithmetic operator.
    /// </summary>
    public virtual bool IsTightOperator(string op) => op is "." or "::";

    /// <summary>A backslash escapes the next character inside a string literal.</summary>
    public virtual bool SupportsBackslashEscapes => false;

    /// <summary><c>GO</c> for SQL Server, <c>/</c> for Oracle, nothing for the rest.</summary>
    public virtual string? BatchSeparator => null;

    public virtual bool IsBatchSeparatorLine(string trimmedLine) =>
        BatchSeparator is not null &&
        trimmedLine.Equals(BatchSeparator, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A line that has to survive as it stands because it is not SQL: a SQL*Plus directive, a
    /// MySQL <c>DELIMITER</c> statement. Only its trailing whitespace is dropped, the same as
    /// everywhere else in the output.
    /// </summary>
    public virtual bool IsPassthroughLine(string trimmedLine) => false;

    /// <summary>
    /// Recognises a statement that changes the terminator, so that <c>;</c> stops ending statements
    /// inside a MySQL routine body.
    /// </summary>
    public virtual bool TryReadDelimiterChange(string trimmedLine, out string delimiter)
    {
        delimiter = string.Empty;
        return false;
    }

    /// <summary>
    /// Gives the dialect first refusal on the text at <paramref name="input"/>, for literal forms
    /// the shared lexer does not know: <c>N'…'</c>, <c>q'[…]'</c>, <c>$tag$…$tag$</c>, <c>0x…</c>.
    /// </summary>
    /// <param name="length">How many characters were consumed.</param>
    public virtual bool TryReadSpecial(ReadOnlySpan<char> input, out SqlTokenKind kind, out int length)
    {
        kind = default;
        length = 0;
        return false;
    }
}
