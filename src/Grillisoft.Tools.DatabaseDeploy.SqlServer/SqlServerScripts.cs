namespace Grillisoft.Tools.DatabaseDeploy.SqlServer;

public class SqlServerScripts(string databaseName, string migrationTableName) : ISqlScripts
{
    public string ExistsSql { get; } = $@"
            SELECT COUNT(*) FROM sys.databases where name='{databaseName}'
        ";

    public string[] CreateSql { get; } = [$@"
            CREATE DATABASE [{databaseName}]
        "];

    public string InitSql { get; } = $@"
            IF OBJECT_ID(N'{migrationTableName}', N'U') IS NULL
            CREATE TABLE {migrationTableName} (
              [id] int NOT NULL IDENTITY(1,1),
              [name] NVARCHAR(255),
              [deployed_utc] datetime2,
              [user] NVARCHAR(100),
              [hash] char(32),
              CONSTRAINT [PK_{migrationTableName}] PRIMARY KEY CLUSTERED([id] ASC),
              CONSTRAINT [AK_{migrationTableName}_name] UNIQUE([name])
            );
        ";

    public string GetMigrationsSql { get; } =
        $"SELECT [name], [deployed_utc], [user], [hash] FROM {migrationTableName} ORDER BY [id] ASC";

    public string AddMigrationSql { get; } =
        $@"INSERT INTO {migrationTableName}([name], [deployed_utc], [user], [hash])
           VALUES(@name, @deployed_utc, @user_name, @hash)";

    public string RemoveMigrationSql { get; } =
        $"DELETE FROM {migrationTableName} WHERE [name] = @name";

    public string ClearMigrationsSql { get; } = $"DROP TABLE IF EXISTS {migrationTableName}";
}