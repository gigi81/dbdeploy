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
}