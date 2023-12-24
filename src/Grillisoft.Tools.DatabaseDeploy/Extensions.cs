using System;
using System.Collections.Generic;
using System.Linq;

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
}