namespace Grillisoft.Tools.DatabaseDeploy.SqlServer.Ddl;

/// <summary>
/// Turning catalog names into T-SQL, and back.
/// </summary>
internal static class StringExtensions
{
    /// <summary>
    /// Wraps an identifier in brackets. A name is allowed to hold a closing bracket, which has to be
    /// doubled, and every other character - including a keyword or a space - is safe once quoted.
    /// </summary>
    public static string Quote(this string name)
        => "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";

    /// <summary>
    /// Quotes an identifier and prefixes its schema, for the objects that have one.
    /// </summary>
    public static string Qualify(this string name, string? schema)
        => string.IsNullOrEmpty(schema) ? name.Quote() : schema.Quote() + "." + name.Quote();

    /// <summary>
    /// Drops the schema prefix and the brackets from a name. The migrations table can be configured
    /// schema qualified, or quoted, or both; the catalog holds neither.
    /// </summary>
    public static string Unqualified(this string name)
    {
        var separator = name.LastIndexOf('.');
        return (separator < 0 ? name : name[(separator + 1)..]).Trim('[', ']');
    }
}
