using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace Grillisoft.Tools.DatabaseDeploy;

internal static class Extensions
{
    public static IEnumerable<string> ExceptIgnoreCase(this IEnumerable<string> first, IEnumerable<string> second)
    {
        return first.Except(second, StringComparer.InvariantCultureIgnoreCase);
    }

    public static Queue<T> ToQueue<T>(this IEnumerable<T> items)
    {
        return new Queue<T>(items);
    }

    public static bool EqualsIgnoreCase(this string obj, string value)
    {
        return obj.Equals(value, StringComparison.InvariantCultureIgnoreCase);
    }

    public static async IAsyncEnumerable<T> WhereAsync<T>(this IEnumerable<T> source, Func<T, CancellationToken, Task<bool>> filter, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var item in source)
        {
            if (await filter.Invoke(item, cancellationToken))
                yield return item;
        }
    }
    
    public static async Task<string> ComputeHash(this IFileInfo file)
    {
        using var md5 = MD5.Create();
        await using var stream = file.OpenRead();
        var data = await md5.ComputeHashAsync(stream);
        var builder = new StringBuilder(32);

        foreach (var b in data)
        {
            builder.Append(b.ToString("x2")); // Convert to hexadecimal
        }

        return builder.ToString();
    }
}