using System.Runtime.CompilerServices;

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
    
    internal static string BranchName(this string filename)
    {
        var name = filename;
        var index = name.LastIndexOf('.');
        if (index >= 0)
            name = name.Substring(0, index);

        name = name.Replace('_', '/');
        return name;
    }
}