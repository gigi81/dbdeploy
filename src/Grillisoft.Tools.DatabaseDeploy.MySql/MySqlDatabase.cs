using System.Data.Common;
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
        int scriptTimeout,
        MySqlScriptParser parser,
        ILogger<MySqlDatabase> logger
    ) : base(name, CreateConnection(connectionString, logger), parser, logger)
    {
        _migrationTableName = migrationTableName;
        this.ScriptTimeout = scriptTimeout;
    }

    protected override ISqlScripts CreateSqlScripts()
    {
        return new MySqlScripts(this.DatabaseName, _migrationTableName);
    }

    protected override DbConnection CreateConnectionWithoutDatabase(ILogger logger)
    {
        var builder = new MySqlConnectionStringBuilder(this.Connection.ConnectionString);
        builder.Database = "";
        return CreateConnection(builder.ConnectionString, logger);
    }

    private static DbConnection CreateConnection(string connectionString, ILogger logger)
    {
        var connection = new MySqlConnection(connectionString);
        connection.InfoMessage += (sender, args) =>
        {
            foreach (var error in args.Errors)
            {
                logger.LogInformation(error.Message);    
            }
            
        };
        return connection;
    }
}