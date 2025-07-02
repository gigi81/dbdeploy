using System.Data.Common;
using Grillisoft.Tools.DatabaseDeploy.Database;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer;

public class SqlServerDatabase : DatabaseBase
{
    private readonly string _migrationTableName;

    internal SqlServerDatabase(
        string name,
        string connectionString,
        string migrationTableName,
        int scriptTimeout,
        SqlServerScriptParser parser,
        ILogger<SqlServerDatabase> logger
    ) : base(name, CreateConnection(connectionString, logger), parser, logger)
    {
        _migrationTableName = migrationTableName;
        this.ScriptTimeout = scriptTimeout;
    }

    public override string Dialect => "Microsoft SQL Server";

    protected override ISqlScripts CreateSqlScripts()
    {
        return new SqlServerScripts(this.DatabaseName, _migrationTableName);
    }

    protected override DbConnection CreateConnectionWithoutDatabase(ILogger logger)
    {
        var builder = new SqlConnectionStringBuilder(this.Connection.ConnectionString);
        builder.InitialCatalog = "";
        return CreateConnection(builder.ConnectionString, logger);
    }

    private static DbConnection CreateConnection(string connectionString, ILogger logger)
    {
        var connection = new SqlConnection(connectionString);
        connection.InfoMessage += (sender, args) => { logger.LogInformation(args.Message); };
        return connection;
    }
}