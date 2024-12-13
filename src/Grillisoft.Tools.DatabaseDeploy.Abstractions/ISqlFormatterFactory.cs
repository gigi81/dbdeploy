namespace Grillisoft.Tools.DatabaseDeploy.Abstractions;

public interface ISqlFormatterFactory
{
    ISqlFormatter GetSqlFormatter(string sqlDialect);
}