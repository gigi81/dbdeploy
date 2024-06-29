namespace Grillisoft.Tools.DatabaseDeploy.SqlServer;

public interface ISqlScripts
{
    string ExistsSql { get; }
    string[] CreateSql { get; }
    string InitSql { get; }
    string ClearMigrationsSql { get; }
    
    string GetMigrationsSql { get; }
    string AddMigrationSql { get; }
    string RemoveMigrationSql { get; }
}