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
        _migrationTableName = GetPublicSchema(migrationTableName);
        this.ScriptTimeout = scriptTimeout;
    }

    public override string Dialect => "PostgreSQL";

    /// <summary>
    /// Gets the migration table name prefixed by "public"
    /// unless a schema is already specified in the <paramref name="migrationTableName"/>
    /// See https://www.postgresql.org/docs/current/ddl-schemas.html#DDL-SCHEMAS-PUBLIC
    /// </summary>
    /// <param name="migrationTableName"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException"></exception>
    private static string GetPublicSchema(string migrationTableName)
    {
        if (string.IsNullOrWhiteSpace(migrationTableName))
            throw new ArgumentNullException(nameof(migrationTableName));

        if (migrationTableName.Contains('.'))
            return migrationTableName;

        return $"public.{migrationTableName}";
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