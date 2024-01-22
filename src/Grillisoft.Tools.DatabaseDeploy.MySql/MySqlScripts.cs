using Grillisoft.Tools.DatabaseDeploy.SqlServer;

namespace Grillisoft.Tools.DatabaseDeploy.MySql;

public class MySqlScripts(string migrationTableName) : ISqlScripts
{
    public string GetSql { get; } =
        $"SELECT `name`, `deployed_utc`, `user`, `hash` FROM `{migrationTableName}` ORDER BY `id` ASC";
    public string AddSql { get; } =
        $@"INSERT INTO `{migrationTableName}`(`name`, `deployed_utc`, `user`, `hash`)
                     VALUES(@name, @deployed_utc, @user, @hash)";
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