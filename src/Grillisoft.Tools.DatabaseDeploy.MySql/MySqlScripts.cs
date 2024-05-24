using Grillisoft.Tools.DatabaseDeploy.SqlServer;

namespace Grillisoft.Tools.DatabaseDeploy.MySql;

public class MySqlScripts(string databaseName, string migrationTableName) : ISqlScripts
{
    public string ExistsSql { get; } = $@"
            SELECT COUNT(*) FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME='{databaseName}'
        ";

    public string CreateSql { get; } = $@"
            CREATE DATABASE `{databaseName}`
        ";

    public string GetSql { get; } =
        $"SELECT `name`, `deployed_utc`, `user`, `hash` FROM `{migrationTableName}` ORDER BY `id` ASC";
    public string AddSql { get; } =
        $@"INSERT INTO `{migrationTableName}`(`name`, `deployed_utc`, `user`, `hash`)
                     VALUES(@name, @deployed_utc, @user_name, @hash)";
    public string RemoveSql { get; } =
        $"DELETE FROM `{migrationTableName}` WHERE `name` = @name";

    public string InitSql { get; } = $@"
            CREATE TABLE IF NOT EXISTS `{migrationTableName}` (
              `id` int NOT NULL AUTO_INCREMENT,
              `name` NVARCHAR(255),
              `deployed_utc` datetime,
              `user` NVARCHAR(100),
              `hash` char(32),
              PRIMARY KEY (`id`),
              UNIQUE(`name`)
            );
        ";
    public string ClearSql { get; } =
        $"DROP TABLE IF EXISTS `{migrationTableName}`";
}