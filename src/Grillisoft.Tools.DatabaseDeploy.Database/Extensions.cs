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

    /// <summary>
    /// "TABLE (12), VIEW (3), INDEX (25)": how many objects of each type there are, in the order
    /// they are written to a generated script. Used in the logs a whole generation is judged from.
    /// </summary>
    /// <param name="types">The type of every object, one entry per object.</param>
    /// <param name="rankOf">Position of a type in the script; lower comes first.</param>
    public static string Breakdown(this IEnumerable<string> types, Func<string, int> rankOf)
    {
        return types.GroupBy(type => type)
                    .Select(group => new KeyValuePair<string, int>(group.Key, group.Count()))
                    .Breakdown(rankOf);
    }

    /// <summary>
    /// Same, for counts that have already been totalled up.
    /// </summary>
    public static string Breakdown(this IEnumerable<KeyValuePair<string, int>> counts, Func<string, int> rankOf)
    {
        return string.Join(", ", counts.OrderBy(count => rankOf(count.Key))
                                       .Select(count => $"{count.Key} ({count.Value})"));
    }

}