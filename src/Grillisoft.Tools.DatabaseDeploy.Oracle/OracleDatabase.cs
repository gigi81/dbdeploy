using System.Data;
using System.Data.Common;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Database;
using Grillisoft.Tools.DatabaseDeploy.Oracle.Ddl;
using Grillisoft.Tools.DatabaseDeploy.Oracle.Formatting;
using Grillisoft.Tools.DatabaseDeploy.SqlServer;
using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle;

public class OracleDatabase : DatabaseBase
{
    private readonly string _migrationTableName;
    private readonly string _schema;

    public OracleDatabase(
        string name,
        string schema,
        string connectionString,
        string migrationTableName,
        int scriptTimeout,
        OracleScriptParser parser,
        ILogger<OracleDatabase> logger
    ) : base(name, CreateConnection(connectionString, logger), parser, logger)
    {
        _migrationTableName = migrationTableName.ToUpperInvariant();
        _schema = (string.IsNullOrEmpty(schema) ? name : schema).ToUpperInvariant();
        this.ScriptTimeout = scriptTimeout;
    }

    public override string Dialect => "Oracle";

    public override ISqlFormatter SqlFormatter => OracleFormatter.Instance;

    public override string DatabaseName => _schema;

    protected override ISqlScripts CreateSqlScripts()
    {
        this.Logger.LogDebug("Database is {Database}", this.DatabaseName);
        return new OracleScripts(this.DatabaseName, _migrationTableName);
    }

    /// <summary>
    /// ODP.NET binds parameters by position unless told otherwise, which silently misbinds - or
    /// fails with ORA-01008 - as soon as a statement names the same parameter more than once.
    /// </summary>
    protected override DbCommand CreateCommand(string script, DbConnection? connection = null)
    {
        var command = base.CreateCommand(script, connection);

        if (command is OracleCommand oracleCommand)
            oracleCommand.BindByName = true;

        return command;
    }

    protected override DbConnection CreateConnectionWithoutDatabase(ILogger logger)
    {
        var builder = new OracleConnectionStringBuilder(this.Connection.ConnectionString);
        builder.UserID = builder.UserID?.Split("/")[0];
        this.Logger.LogDebug("Setting connection user id to {UserId}", builder.UserID);
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

    public async override Task ClearMigrations(CancellationToken cancellationToken)
    {
        var exists = await RunScript<decimal>(((OracleScripts)this.SqlScripts).MigrationTableExistsSql, cancellationToken);
        if (exists > 0)
            await base.ClearMigrations(cancellationToken);
    }

    public async override Task InitializeMigrations(CancellationToken cancellationToken)
    {
        var count = await this.RunScript<decimal>(((OracleScripts)this.SqlScripts).MigrationTableExistsSql, cancellationToken);
        this.Logger.LogDebug("Found {Count} migration table(s) named {MigrationTable}", count, _migrationTableName);
        if (count > 0)
            return;

        await base.InitializeMigrations(cancellationToken);
    }

    protected async override Task OpenConnection(CancellationToken cancellationToken)
    {
        if (this.Connection.State == ConnectionState.Open)
            return;

        await base.OpenConnection(cancellationToken);
        await using var command = CreateCommand(((OracleScripts)this.SqlScripts).SetSchemaSql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async override Task GenerateSchemaDdl(StreamWriter writer, CancellationToken cancellationToken)
    {
        await OpenConnection(cancellationToken);

        var generator = new OracleSchemaDdlGenerator(
            script => CreateCommand(script),
            _schema,
            _migrationTableName,
            this.Logger);

        await generator.Generate(writer, cancellationToken);
    }
}
