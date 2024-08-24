using System.Runtime.CompilerServices;

namespace Grillisoft.Tools.DatabaseDeploy;

internal static class Extensions
{
    public static Queue<T> ToQueue<T>(this IEnumerable<T> items)
    {
        return new Queue<T>(items);
    }

    public static bool EqualsIgnoreCase(this string obj, string value)
    {
        return obj.Equals(value, StringComparison.InvariantCultureIgnoreCase);
    }

    public static HashSet<string> ToHashSetIgnoreCase(this IEnumerable<string> source)
    {
        return new HashSet<string>(source, StringComparer.InvariantCultureIgnoreCase);
    }

    public static string OverrideWith(this string defaultValue, string? value)
    {
        return string.IsNullOrEmpty(value) ? defaultValue : value;
    }

    public static string Truncate(this string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Substring(0, Math.Min(maxLength, value.Length));
    }

    internal static string BranchName(this string filename)
    {
        var name = filename;
        var index = name.LastIndexOf('.');
        if (index >= 0)
            name = name.Substring(0, index);

        name = name.Replace('_', '/');
        return name;
    }

    public static async IAsyncEnumerable<T> WhereAsync<T>(this IEnumerable<T> source, Func<T, CancellationToken, Task<bool>> filter, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var item in source)
        {
            if (await filter.Invoke(item, cancellationToken))
                yield return item;
        }
    }
}