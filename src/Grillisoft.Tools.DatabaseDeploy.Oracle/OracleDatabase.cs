using System.Data;
using System.Data.Common;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Database;
using Grillisoft.Tools.DatabaseDeploy.SqlServer;
using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;
using Soenneker.Extensions.String;

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
            await foreach (var ddl in GetObjectDdl(dbObject.Name, dbObject.Type, cancellationToken))
            {
                await writer.WriteLineAsync(ddl.Trim());
                await writer.WriteLineAsync("/");
                await writer.WriteLineAsync();
            }
        }

        foreach (var ddl in await GetCommentsDdl(cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(ddl))
                continue;

            await writer.WriteLineAsync(ddl.Trim());
            await writer.WriteLineAsync("/");
            await writer.WriteLineAsync();
        }
    }

    internal static readonly string[] OracleObjectTypes = [
        "TYPE",
        "TABLE",
        "SEQUENCE",
        "INDEX",
        "CONSTRAINT",
        "REF_CONSTRAINT",
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
            -- exclude sequences generated by oracle itself
            AND OBJECT_NAME NOT LIKE 'ISEQ$$%'
            -- exclude everything related to migration table
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
            -- exclude primary keys as they will be generated along with the tables DDLs
             AND OBJECT_NAME NOT IN
             (
                 SELECT
                     constraint_name
                 FROM
                     all_constraints
                 WHERE
                     constraint_type = 'P'
                 AND
                     owner = :OWNER
             )
            -- exclude constrain indexes as they will be generated along with the tables DDLs
            AND OBJECT_NAME NOT IN
            (
                SELECT
    	            index_name
                FROM
    	            all_indexes
                WHERE
    	            constraint_index = 'YES'
    	            AND OWNER = :OWNER
            )
        UNION
        SELECT
            CONSTRAINT_NAME as OBJECT_NAME,
            'REF_CONSTRAINT' as OBJECT_TYPE
        FROM
            ALL_CONSTRAINTS
        WHERE
            OWNER = :OWNER
            AND constraint_type = 'R'
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

    private const string DllTransformSql = """
        BEGIN
          DBMS_METADATA.SET_TRANSFORM_PARAM(DBMS_METADATA.SESSION_TRANSFORM, 'CONSTRAINTS', TRUE);
          DBMS_METADATA.SET_TRANSFORM_PARAM(DBMS_METADATA.SESSION_TRANSFORM, 'REF_CONSTRAINTS', FALSE);
          DBMS_METADATA.SET_TRANSFORM_PARAM(DBMS_METADATA.SESSION_TRANSFORM, 'STORAGE', FALSE);
          DBMS_METADATA.SET_TRANSFORM_PARAM(DBMS_METADATA.SESSION_TRANSFORM, 'EMIT_SCHEMA', FALSE);
        END;
    """;
    
    private async IAsyncEnumerable<string> GetObjectDdl(string objectName, string objectType, CancellationToken cancellationToken)
    {
        Logger.LogInformation("Getting DDL for object {ObjectName} of type {ObjectType}", objectName, objectType);
        await using var disableConstraintsCommand = CreateCommand(DllTransformSql);
        await disableConstraintsCommand.ExecuteNonQueryAsync(cancellationToken);
        
        await using var command = CreateCommand(GetObjectDdlSql);

        command.AddParameter("object_type", objectType);
        command.AddParameter("object_name", objectName);
        command.AddParameter("owner", _schema);

        var ddl = await command.ExecuteScalarAsync(cancellationToken) as string;
        if(string.IsNullOrWhiteSpace(ddl))
            yield break;

        if (!objectType.EqualsIgnoreCase("TABLE"))
        {
            yield return ddl;
            yield break;
        }
        
        var indexes = ddl.AllIndexes(["ALTER TABLE", "CREATE UNIQUE INDEX"], StringComparison.OrdinalIgnoreCase)
            .Where(i => i > 0)
            .Order()
            .ToList();
        
        if (indexes.Count == 0)
        {
            yield return ddl;
        }
        else
        {
            var last = 0;
            foreach (var index in indexes)
            {
                if(index - last > 0)
                    yield return ddl.Substring(last, index - last);
                last = index;
            }

            yield return ddl.Substring(last);
        }
    }

    private const string GetCommentsDdlSql = """
         SELECT 'COMMENT ON TABLE "' || tc.table_name || '" IS ''' || REPLACE(tc.comments, '''', '''''') || ''';' AS ddl
         FROM all_tab_comments tc
         WHERE tc.owner = :OWNER
           AND tc.comments IS NOT NULL
         
         UNION ALL
         
         SELECT 'COMMENT ON COLUMN "' || cc.table_name || '"."' || cc.column_name || '" IS ''' || REPLACE(cc.comments, '''', '''''') || ''';' AS ddl
         FROM all_col_comments cc
         WHERE cc.owner = :OWNER
           AND cc.comments IS NOT NULL
         
         UNION ALL
         
         SELECT 'COMMENT ON INDEXTYPE "' || it.indextype_name || '" IS ''' || REPLACE(it.comments, '''', '''''') || ''';' AS ddl
         FROM all_indextype_comments it
         WHERE it.owner = :OWNER
           AND it.comments IS NOT NULL
         
         UNION ALL
         
         SELECT 'COMMENT ON OPERATOR "' || op.operator_name || '" IS ''' || REPLACE(op.comments, '''', '''''') || ''';' AS ddl
         FROM all_operator_comments op
         WHERE op.owner = :OWNER
           AND op.comments IS NOT NULL
         
         UNION ALL
         
         SELECT 'COMMENT ON EDITION "' || ed.edition_name || '" IS ''' || REPLACE(ed.comments, '''', '''''') || ''';' AS ddl
         FROM all_edition_comments ed
         WHERE ed.comments IS NOT NULL
     """;
    
    private async Task<List<string>> GetCommentsDdl(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Getting comments DDL");
        await using var command = CreateCommand(GetCommentsDdlSql);

        command.AddParameter("owner", _schema);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        
        var ret = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            ret.Add(reader.GetString(0));
        }
        return ret;
    }
}
