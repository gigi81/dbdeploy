using System.Data.Common;

namespace Grillisoft.Tools.DatabaseDeploy.Database;

public static class Extensions
{
    public static DbCommand AddParameter(this DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
        return command;
    }

    public static IEnumerable<int> AllIndexes(this string value, string search)
    {
        return value.AllIndexes(search, StringComparison.OrdinalIgnoreCase);
    }

    public static IEnumerable<int> AllIndexes(this string value, IEnumerable<string> searches)
    {
        return value.AllIndexes(searches, StringComparison.OrdinalIgnoreCase);
    }

    public static IEnumerable<int> AllIndexes(this string value, IEnumerable<string> searches, StringComparison comparison)
    {
        var ret = new List<int>();

        foreach (var search in searches)
        {
            ret.AddRange(value.AllIndexes(search, comparison));
        }

        return ret;
    }

    public static IEnumerable<int> AllIndexes(this string value, string search, StringComparison comparison)
    {
        var index = value.IndexOf(search, comparison);

        while (index >= 0)
        {
            yield return index;
            index = value.IndexOf(search, index + search.Length, comparison);
        }
    }
}