namespace Grillisoft.Tools.DatabaseDeploy.Database.Formatting;

public enum SqlTokenKind
{
    /// <summary>Spaces and tabs. Never contains a line break.</summary>
    Whitespace,

    /// <summary>One line break, however it was written in the source.</summary>
    Newline,

    /// <summary><c>--</c> or, where the dialect allows it, <c>#</c> to the end of the line.</summary>
    LineComment,

    /// <summary><c>/* … */</c>.</summary>
    BlockComment,

    /// <summary>A quoted string, including any dialect prefix such as <c>N</c> or <c>q</c>.</summary>
    StringLiteral,

    /// <summary>A delimited identifier: <c>[x]</c>, <c>"x"</c> or <c>`x`</c>.</summary>
    QuotedIdentifier,

    Number,

    /// <summary>A bare word: a keyword, a function name or an identifier.</summary>
    Word,

    Operator,

    Comma,

    /// <summary>The statement terminator currently in force, usually <c>;</c>.</summary>
    Terminator,

    OpenParen,

    CloseParen,

    /// <summary>A bind variable or parameter marker: <c>@x</c>, <c>:x</c>, <c>$1</c>, <c>?</c>.</summary>
    Placeholder,

    /// <summary>
    /// A whole line the formatter must reproduce byte for byte: a SQL*Plus directive, a MySQL
    /// <c>DELIMITER</c> statement.
    /// </summary>
    Passthrough,

    /// <summary>A batch separator on a line of its own: <c>GO</c> or <c>/</c>.</summary>
    BatchSeparator
}
