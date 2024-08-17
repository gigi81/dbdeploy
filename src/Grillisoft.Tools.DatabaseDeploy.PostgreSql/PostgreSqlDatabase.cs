using System.Data.Common;
using Grillisoft.Tools.DatabaseDeploy.Database;
using Grillisoft.Tools.DatabaseDeploy.SqlServer;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Grillisoft.Tools.DatabaseDeploy.PostgreSql;

public class PostgreSqlDatabase : DatabaseBase
{
    private readonly string _migrationTableName;

    public PostgreSqlDatabase(
        string name,
        string connectionString,
        string migrationTableName,
        int scriptTimeout,
        PostgreSqlScriptParser parser,
        ILogger<PostgreSqlDatabase> logger
        )
        : base(name, CreateConnection(connectionString, logger), parser, logger)
    {
        _migrationTableName = migrationTableName;
        this.ScriptTimeout = scriptTimeout;
    }

    private static DbConnection CreateConnection(string connectionString, ILogger logger)
    {
        var connection = new NpgsqlConnection(connectionString);
        connection.Notice += (_, args) => { logger.LogInformation("{Message}", args.Notice.MessageText); };
        return connection;
    }

    protected override ISqlScripts CreateSqlScripts()
    {
        return new PostgreSqlScripts(this.DatabaseName, _migrationTableName);
    }

    protected override DbConnection CreateConnectionWithoutDatabase(ILogger logger)
    {
        var builder = new NpgsqlConnectionStringBuilder(this.Connection.ConnectionString);
        builder.Database = "";
        return CreateConnection(builder.ConnectionString, logger);
    }
}