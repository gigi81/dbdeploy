using Grillisoft.Tools.DatabaseDeploy.SqlServer;

namespace Grillisoft.Tools.DatabaseDeploy.PostgreSql;

public class PostgreSqlScripts(string databaseName, string migrationTableName) : ISqlScripts
{
    public string ExistsSql { get; } = $@"
            SELECT COUNT(*) FROM pg_catalog.pg_database WHERE datname='{databaseName}'
        ";

    public string[] CreateSql { get; } = [$@"CREATE DATABASE ""{databaseName}"""];

    public string GetMigrationsSql { get; } =
        $@"SELECT ""name"", ""deployed_utc"", ""user"", ""hash"" FROM ""{migrationTableName}"" ORDER BY ""id"" ASC";
    public string AddMigrationSql { get; } =
        $@"INSERT INTO ""{migrationTableName}""(""name"", ""deployed_utc"", ""user"", ""hash"")
                     VALUES(@name, @deployed_utc, @user_name, @hash)";
    public string RemoveMigrationSql { get; } =
        $@"DELETE FROM ""{migrationTableName}"" WHERE ""name"" = @name";

    public string InitSql { get; } = $@"
            CREATE TABLE IF NOT EXISTS ""{migrationTableName}"" (
              ""id"" integer primary key generated always as identity,
              ""name"" varchar(255),
              ""deployed_utc"" timestamp,
              ""user"" varchar(100),
              ""hash"" char(32),
              UNIQUE(""name"")
            );
        ";
    public string ClearMigrationsSql { get; } =
        $@"DROP TABLE IF EXISTS ""{migrationTableName}""";
}