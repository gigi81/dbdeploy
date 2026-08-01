namespace Grillisoft.Tools.DatabaseDeploy;

internal static class Extensions
{
    public static Queue<T> ToQueue<T>(this IEnumerable<T> items)
    {
        return new Queue<T>(items);
    }

    public static string OverrideWith(this string defaultValue, string? value)
    {
        return string.IsNullOrEmpty(value) ? defaultValue : value;
    }

    /// <summary>
    /// Same as <see cref="OverrideWith"/> for the settings where an empty value is an instruction
    /// rather than a value nobody set: only a value that is not there at all keeps the default.
    /// </summary>
    public static string OverrideWithAllowEmpty(this string defaultValue, string? value)
    {
        return value ?? defaultValue;
    }
}