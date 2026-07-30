using System.Text;

namespace Grillisoft.Tools.DatabaseDeploy.Abstractions;

/// <summary>
/// The knobs the formatter reads, resolved per file from the script repository's own
/// <c>.editorconfig</c>. The defaults here are what a repository with no <c>[*.sql]</c> section
/// gets.
/// </summary>
public sealed record SqlFormatterOptions
{
    public static readonly SqlFormatterOptions Default = new();

    /// <summary>One indentation level, from <c>indent_style</c> and <c>indent_size</c>.</summary>
    public string Indent { get; init; } = "    ";

    /// <summary>From <c>end_of_line</c>; defaults to the ending the file already uses.</summary>
    public string NewLine { get; init; } = "\n";

    /// <summary>From <c>charset</c>.</summary>
    public Encoding Encoding { get; init; } = new UTF8Encoding(false);

    /// <summary>
    /// From <c>max_line_length</c>. A parenthesised group longer than this is broken over several
    /// lines instead of being kept inline.
    /// </summary>
    public int MaxLineLength { get; init; } = 120;

    public SqlCase KeywordCase { get; init; } = SqlCase.Upper;

    public SqlCase DataTypeCase { get; init; } = SqlCase.Upper;

    /// <summary>Applies to built-in functions only, never to the author's own routines.</summary>
    public SqlCase FunctionCase { get; init; } = SqlCase.Upper;

    public SqlCase BatchSeparatorCase { get; init; } = SqlCase.Upper;

    public int BlankLinesBetweenStatements { get; init; } = 1;

    public bool InsertFinalNewline { get; init; } = true;

    public bool TrimTrailingWhitespace { get; init; } = true;

    /// <summary>
    /// From <c>dbdeploy_sql_enabled</c>. The per-glob escape hatch for scripts that must not be
    /// touched, such as vendor dumps.
    /// </summary>
    public bool Enabled { get; init; } = true;
}
