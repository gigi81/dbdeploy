using Grillisoft.Tools.DatabaseDeploy.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.Formatting;

public class SqlFormatterFactory : ISqlFormatterFactory
{
    public ISqlFormatter GetSqlFormatter(string sqlDialect)
    {
        return new SqlFormatter();
    }
}