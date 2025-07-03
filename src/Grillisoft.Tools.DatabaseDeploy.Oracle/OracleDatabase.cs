using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Text;
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
        _migrationTableName = migrationTableName;
        _schema = string.IsNullOrEmpty(schema) ? name : schema;
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

    private static readonly string[] OracleObjectTypes = {
        "TABLE", "VIEW", "PROCEDURE", "FUNCTION", "SEQUENCE", "INDEX",
        "PACKAGE", "PACKAGE BODY", "TRIGGER", "TYPE", "SYNONYM"
    };

    private static readonly string GetObjectsListSql = $"""
        SELECT
            OBJECT_NAME,
            OBJECT_TYPE
        FROM
            ALL_OBJECTS
        WHERE
            OWNER = :owner
          AND OBJECT_TYPE IN ({String.Join(",", OracleObjectTypes.Select(t => $"'{t}'"))})
        ORDER BY
            OBJECT_TYPE,
            OBJECT_NAME
    """;
    
    public async override Task GenerateSchemaDdl(Stream stream, CancellationToken cancellationToken)
    {
        await OpenConnection(cancellationToken);
        await using var writer = new StreamWriter(stream, Encoding.UTF8, 1024, true);

        Logger.LogInformation("Getting list of objects for schema {SchemaName}", _schema);
        await using var command = CreateCommand(GetObjectsListSql);
        command.AddParameter("owner", _schema);

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

        var dependencies = await GetDependencies(cancellationToken);
        var sortedObjects = TopologicalSort(objectsToScript.Select(o => new DbObject(o.Name, o.Type)).ToList(), dependencies);

        foreach (var dbObject in sortedObjects)
        {
            var ddl = await GetObjectDDL(dbObject.Name, dbObject.Type, cancellationToken);
            if (ddl == null || ddl == DBNull.Value)
                continue;

            await writer.WriteLineAsync(ddl.ToString()?.Trim());
            await writer.WriteLineAsync("/");
            await writer.WriteLineAsync();
        }
    }

    private const string GetDdlSql = "SELECT DBMS_METADATA.GET_DDL(:object_type, :object_name, :owner) FROM DUAL";

    private async Task<object?> GetObjectDDL(string objectName, string objectType, CancellationToken cancellationToken)
    {
        Logger.LogInformation("Getting DDL for object {ObjectName} of type {ObjectType}", objectName, objectType);
        await using var command = CreateCommand(GetDdlSql);

        command.AddParameter("object_type", objectType);
        command.AddParameter("object_name", objectName);
        command.AddParameter("owner", _schema);

        return await command.ExecuteScalarAsync(cancellationToken);
    }

    private const string GetDependenciesSql = "SELECT NAME, TYPE, REFERENCED_NAME, REFERENCED_TYPE FROM ALL_DEPENDENCIES WHERE OWNER = :owner AND REFERENCED_OWNER = :owner";
    
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
                reader.GetString(0), 
                reader.GetString(1), 
                reader.GetString(2),
                reader.GetString(3)));
        }

        return dependencies;
    }

    private List<DbObject> TopologicalSort(List<DbObject> objects, List<OracleObjectDependencies> dependencies)
    {
        var graph = new Dictionary<DbObject, List<DbObject>>();
        var inDegree = new Dictionary<DbObject, int>();

        foreach (var obj in objects)
        {
            graph[obj] = new List<DbObject>();
            inDegree[obj] = 0;
        }

        foreach (var dep in dependencies)
        {
            var source = objects.FirstOrDefault(o => o.Name == dep.Name && o.Type == dep.Type);
            var target = objects.FirstOrDefault(o => o.Name == dep.ReferencedName && o.Type == dep.ReferencedType);

            if (source != null && target != null)
            {
                graph[target].Add(source);
                inDegree[source]++;
            }
        }

        var queue = new Queue<DbObject>(objects.Where(obj => inDegree[obj] == 0));
        var sortedList = new List<DbObject>();

        while (queue.Any())
        {
            var obj = queue.Dequeue();
            sortedList.Add(obj);

            foreach (var neighbor in graph[obj])
            {
                inDegree[neighbor]--;
                if (inDegree[neighbor] == 0)
                {
                    queue.Enqueue(neighbor);
                }
            }
        }

        // If sortedList doesn't contain all objects, there's a cycle
        if (sortedList.Count != objects.Count)
        {
            // Handle cycle detection, e.g., log a warning or throw an exception
            Logger.LogWarning("Cycle detected in database object dependencies. DDL generation might be incomplete.");
        }

        return sortedList;
    }
}
