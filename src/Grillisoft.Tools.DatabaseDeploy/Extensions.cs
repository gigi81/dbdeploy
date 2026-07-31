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
}