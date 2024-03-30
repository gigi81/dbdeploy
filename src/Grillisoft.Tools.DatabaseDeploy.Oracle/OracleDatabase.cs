using System.Data.Common;
using Grillisoft.Tools.DatabaseDeploy.Database;
using Grillisoft.Tools.DatabaseDeploy.SqlServer;
using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle;

public class OracleDatabase : DatabaseBase
{
    private readonly string _migrationTableName;

    public OracleDatabase(
        string name,
        string connectionString,
        string migrationTableName,
        int scriptTimeout,
        OracleScriptParser parser,
        ILogger<OracleDatabase> logger
    ) : base(name, CreateConnection(connectionString, logger), parser, logger)
    {
        _migrationTableName = migrationTableName;
        this.ScriptTimeout = scriptTimeout;
    }

    protected override ISqlScripts CreateSqlScripts()
    {
        return new OracleScripts(this.DatabaseName, _migrationTableName);
    }

    protected override DbConnection CreateConnectionWithoutDatabase(ILogger logger)
    {
        var builder = new OracleConnectionStringBuilder(this.Connection.ConnectionString);
        builder.DataSource = "";
        return CreateConnection(builder.ConnectionString, logger);
    }

    private static DbConnection CreateConnection(string connectionString, ILogger logger)
    {
        var connection = new OracleConnection(connectionString);
        connection.InfoMessage += (sender, args) =>
        {
            foreach (OracleError error in args.Errors)
            {
                logger.LogInformation(error.Message);    
            }
            
        };
        return connection;
    }
}