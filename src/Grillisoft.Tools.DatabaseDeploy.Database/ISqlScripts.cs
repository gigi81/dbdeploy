namespace Grillisoft.Tools.DatabaseDeploy.SqlServer;

public interface ISqlScripts
{
    string InitSql { get; }
    string GetSql { get; }
    string AddSql { get; }
    string RemoveSql { get; }
    string ClearSql { get; }
}