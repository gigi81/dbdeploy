using System.Data.Common;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Database;
using Grillisoft.Tools.DatabaseDeploy.SqlServer.Ddl;
using Grillisoft.Tools.DatabaseDeploy.SqlServer.Formatting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer;

public class SqlServerDatabase : DatabaseBase
{
    private readonly string _migrationTableName;

    /// <summary>
    /// Kept because <see cref="SqlConnection.ConnectionString"/> drops the password once the
    /// connection has been opened, and the DDL generation needs a connection of its own.
    /// </summary>
    private readonly string _connectionString;

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
        _connectionString = connectionString;
        this.ScriptTimeout = scriptTimeout;
    }

    public override string Dialect => "Microsoft SQL Server";

    public override ISqlFormatter SqlFormatter => SqlServerFormatter.Instance;

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

    public async override Task GenerateSchemaDdl(StreamWriter writer, CancellationToken cancellationToken)
    {
        await OpenConnection(cancellationToken);

        using var generator = new SqlServerSchemaDdlGenerator(
            script => CreateCommand(script),
            _connectionString,
            this.DatabaseName,
            _migrationTableName,
            this.Logger);

        await generator.Generate(writer, cancellationToken);
    }
}