namespace Grillisoft.Tools.DatabaseDeploy.MySql.Ddl;

/// <summary>
/// Turning catalog names into SQL, and back.
/// </summary>
internal static class StringExtensions
{
    /// <summary>
    /// Wraps an identifier in backticks. A name is allowed to hold a backtick, which has to be
    /// doubled, and every other character - including a keyword or a space - is safe once quoted.
    /// </summary>
    /// <remarks>
    /// <c>SHOW CREATE ...</c> takes no parameters, so every object name reaches the server inside
    /// the statement text. This is what keeps that safe, not a nicety.
    /// </remarks>
    public static string Quote(this string name)
        => "`" + name.Replace("`", "``", StringComparison.Ordinal) + "`";

    /// <summary>
    /// Drops the database prefix and the backticks from a name. The migrations table can be
    /// configured qualified, or quoted, or both; <c>information_schema</c> holds neither.
    /// </summary>
    public static string Unqualified(this string name)
    {
        var separator = name.LastIndexOf('.');
        return (separator < 0 ? name : name[(separator + 1)..]).Trim('`');
    }
}
