using Grillisoft.Tools.DatabaseDeploy.Database;
using Grillisoft.Tools.DatabaseDeploy.SqlServer;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace Grillisoft.Tools.DatabaseDeploy.MySql;

public class MySqlDatabase : DatabaseBase
{
    private readonly string _migrationTableName;

    public MySqlDatabase(
        string name,
        string connectionString,
        string migrationTableName,
        MySqlScriptParser parser,
        ILogger<MySqlDatabase> logger
    ) : base(name, new MySqlConnection(connectionString), parser, logger)
    {
        _migrationTableName = migrationTableName;
    }

    protected override ISqlScripts CreateSqlScripts()
    {
        return new MySqlScripts(this.DatabaseName, _migrationTableName);
    }
}