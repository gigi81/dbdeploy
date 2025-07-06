using System.Data;
using System.Data.Common;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Database;
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

    public override string DatabaseName => _schema;

    protected override ISqlScripts CreateSqlScripts()
    {
        this.Logger.LogWarning("Database is {Database}", this.DatabaseName);
        return new OracleScripts(this.DatabaseName, _migrationTableName);
    }

    protected override DbConnection CreateConnectionWithoutDatabase(ILogger logger)
    {
        var builder = new OracleConnectionStringBuilder(this.Connection.ConnectionString);
        builder.UserID = builder.UserID?.Split("/")[0];
        this.Logger.LogWarning("Setting connection user id to {UserId}", builder.UserID);
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
        this.Logger.LogWarning("Count {Count}", count);
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

        var dbObjects = await GetObjectsList(cancellationToken);
        var dbObjectsDependencies = await GetDependencies(cancellationToken);
        
        Logger.LogInformation("Building graph");
        var graph = new OracleObjectsGraph(dbObjects, dbObjectsDependencies);

        Logger.LogInformation("Starting DDL generation");
        foreach (var dbObject in graph.GetGraph())
        {
            var ddl = await GetObjectDdl(dbObject.Name, dbObject.Type, cancellationToken);
            if (ddl == null || ddl == DBNull.Value)
                continue;

            await writer.WriteLineAsync(ddl.ToString()?.Trim());
            await writer.WriteLineAsync("/");
            await writer.WriteLineAsync();
        }
    }

    internal static readonly string[] OracleObjectTypes = [
        "TYPE",
        "TABLE",
        "SEQUENCE",
        //"INDEX",
        "TRIGGER",
        "SYNONYM",
        "VIEW",
        "FUNCTION",
        "PROCEDURE",
        "PACKAGE",
        "PACKAGE BODY",
    ];

    private static readonly string GetObjectsListSql = $"""
        SELECT
            OBJECT_NAME,
            OBJECT_TYPE
        FROM
            ALL_OBJECTS
        WHERE
            OWNER = :OWNER
            AND OBJECT_TYPE IN ({String.Join(",", OracleObjectTypes.Select(t => $"'{t}'"))})
            AND OBJECT_NAME NOT LIKE 'ISEQ$$%'
            AND OBJECT_NAME != :MIGRATION_TABLE_NAME
            AND OBJECT_NAME NOT IN
            (
                SELECT 
                    index_name
                FROM
                    all_indexes
                WHERE
                    owner = :OWNER
                    AND table_name = :MIGRATION_TABLE_NAME
            )
    """;
    
    private async Task<List<DbObject>> GetObjectsList(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Getting list of objects for schema {SchemaName}", _schema);
        await using var command = CreateCommand(GetObjectsListSql);
        command.AddParameter("OWNER", _schema);
        command.AddParameter("MIGRATION_TABLE_NAME", _migrationTableName);

        var objectsToScript = new List<DbObject>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                objectsToScript.Add(new DbObject(
                    reader.GetString(0), 
                    reader.GetString(1)));
            }
        }

        return objectsToScript;
    }

    private const string GetDependenciesSql = """
        SELECT
            NAME,
            TYPE,
            REFERENCED_NAME,
            REFERENCED_TYPE
        FROM
            ALL_DEPENDENCIES
        WHERE
            OWNER = :owner
            AND REFERENCED_OWNER = :owner
        """;
    
    private async Task<List<OracleObjectDependencies>> GetDependencies(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Getting objects dependencies for schema {SchemaName}", _schema);
        await using var command = CreateCommand(GetDependenciesSql);
        command.AddParameter("owner", _schema);

        var dependencies = new List<OracleObjectDependencies>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            dependencies.Add(new OracleObjectDependencies(
                new DbObject(reader.GetString(0), reader.GetString(1)),
                new DbObject(reader.GetString(2), reader.GetString(3))));
        }

        return dependencies;
    }
    
    private const string GetObjectDdlSql = "SELECT DBMS_METADATA.GET_DDL(:object_type, :object_name, :owner) FROM DUAL";

    private const string DisableConstraintsSql = """
        BEGIN
          DBMS_METADATA.SET_TRANSFORM_PARAM(DBMS_METADATA.SESSION_TRANSFORM, 'REF_CONSTRAINTS', FALSE);
          DBMS_METADATA.SET_TRANSFORM_PARAM(DBMS_METADATA.SESSION_TRANSFORM, 'STORAGE', FALSE);
          DBMS_METADATA.SET_TRANSFORM_PARAM(DBMS_METADATA.SESSION_TRANSFORM, 'EMIT_SCHEMA', FALSE);
        END;
    """;
    
    private async Task<object?> GetObjectDdl(string objectName, string objectType, CancellationToken cancellationToken)
    {
        Logger.LogInformation("Getting DDL for object {ObjectName} of type {ObjectType}", objectName, objectType);
        await using var disableConstraintsCommand = CreateCommand(DisableConstraintsSql);
        await disableConstraintsCommand.ExecuteNonQueryAsync(cancellationToken);
        
        await using var command = CreateCommand(GetObjectDdlSql);

        command.AddParameter("object_type", objectType);
        command.AddParameter("object_name", objectName);
        command.AddParameter("owner", _schema);

        return await command.ExecuteScalarAsync(cancellationToken);
    }
}
