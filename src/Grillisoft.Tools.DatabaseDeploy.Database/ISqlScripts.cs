namespace Grillisoft.Tools.DatabaseDeploy.SqlServer;

public interface ISqlScripts
{
    string ExistsSql { get; }
    string CreateSql { get; }
    string InitSql { get; }
    string GetSql { get; }
    string AddSql { get; }
    string RemoveSql { get; }
    string ClearSql { get; }
}