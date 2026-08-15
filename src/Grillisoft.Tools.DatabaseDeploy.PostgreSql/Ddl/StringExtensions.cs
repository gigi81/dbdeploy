namespace Grillisoft.Tools.DatabaseDeploy.PostgreSql.Ddl;

/// <summary>
/// Turning catalog names into SQL, and back.
/// </summary>
internal static class StringExtensions
{
    /// <summary>
    /// Wraps an identifier in double quotes, doubling any it holds. Everything is quoted rather
    /// than only what needs it: PostgreSQL folds an unquoted name to lower case, so a table called
    /// <c>Orders</c> only comes back as itself if it is quoted every time it is written.
    /// </summary>
    public static string Quote(this string name)
        => "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    /// <summary>Quotes an identifier and prefixes its schema.</summary>
    public static string Qualify(this string name, string? schema)
        => string.IsNullOrEmpty(schema) ? name.Quote() : schema.Quote() + "." + name.Quote();

    /// <summary>
    /// Wraps a value in single quotes for a statement that cannot take a parameter, doubling any
    /// quote it holds.
    /// </summary>
    public static string ToSqlLiteral(this string value)
        => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    /// <summary>
    /// The name without its schema prefix or its quotes. The migrations table arrives already
    /// prefixed - see <see cref="PostgreSqlDatabase"/> - and can have been configured qualified,
    /// quoted, or both; <c>pg_catalog</c> holds neither.
    /// </summary>
    public static string Unqualified(this string name)
    {
        var separator = name.LastIndexOf('.');
        return (separator < 0 ? name : name[(separator + 1)..]).Trim('"');
    }

    /// <summary>
    /// The schema part of a qualified name, or <c>null</c> when there is none.
    /// </summary>
    public static string? SchemaOf(this string name)
    {
        var separator = name.LastIndexOf('.');
        return separator < 0 ? null : name[..separator].Trim('"');
    }
}
