namespace Grillisoft.Tools.DatabaseDeploy.Oracle.Ddl;

/// <summary>
/// Turning dictionary values into PL/SQL.
/// </summary>
internal static class StringExtensions
{
    /// <summary>
    /// Wraps an identifier in double quotes, which is what keeps the name exactly as the dictionary
    /// spells it instead of letting the server fold it to upper case.
    /// </summary>
    public static string Quote(this string name)
        => "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    /// <summary>
    /// Wraps a value in single quotes, doubling the ones it already holds. Comments are written
    /// straight into the script rather than bound, so an apostrophe in one would otherwise end the
    /// literal early and leave the rest of the comment to be read as SQL.
    /// </summary>
    public static string ToSqlLiteral(this string value)
        => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
