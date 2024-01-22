using Grillisoft.Tools.DatabaseDeploy.Database;
using MySqlConnector;

namespace Grillisoft.Tools.DatabaseDeploy.MySql;

public class MySqlDatabase : DatabaseBase
{
    public MySqlDatabase(
        string name,
        string connectionString,
        string migrationTableName,
        MySqlScriptParser parser
    ) : base(name, new MySqlConnection(connectionString), new MySqlScripts(migrationTableName), parser)
    {
    }
}