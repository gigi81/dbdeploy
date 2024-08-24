using System.Data;
using System.Data.Common;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.SqlServer;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.Database;

public abstract class DatabaseBase : IDatabase
{
    private readonly string _name;
    private readonly DbConnection _connection;
    private readonly IScriptParser _parser;
    private readonly ILogger _logger;
    private ISqlScripts? _sqlScripts;

    protected DatabaseBase(
        string name,
        DbConnection connection,
        IScriptParser parser,
        ILogger logger)
    {
        _name = name;
        _connection = connection;
        _parser = parser;
        _logger = new DatabaseLogger(name, logger);
    }

    protected abstract ISqlScripts CreateSqlScripts();

    protected abstract DbConnection CreateConnectionWithoutDatabase(ILogger logger);

    protected ISqlScripts SqlScripts => _sqlScripts ??= CreateSqlScripts();

    public string Name => _name;

    public virtual string DatabaseName => !string.IsNullOrWhiteSpace(_connection.Database) ? _connection.Database : _name;

    public IScriptParser ScriptParser => _parser;

    public int ScriptTimeout { get; set; } = 60 * 60;

    protected DbConnection Connection => _connection;

    protected ILogger Logger => _logger;

    public async virtual Task<bool> Exists(CancellationToken cancellationToken)
    {
        var script = this.SqlScripts.ExistsSql;
        await using var connection = this.CreateConnectionWithoutDatabase(_logger);
        await connection.OpenAsync(cancellationToken);
        await using var command = this.CreateCommand(script, connection);
        var exists = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(exists) == 1;
    }

    public async virtual Task Create(CancellationToken cancellationToken)
    {
        await using var connection = this.CreateConnectionWithoutDatabase(_logger);
        await connection.OpenAsync(cancellationToken);
        foreach (var script in this.SqlScripts.CreateSql)
        {
            await using var command = this.CreateCommand(script, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async virtual Task RunScript(string script, CancellationToken cancellationToken)
    {
        await OpenConnection(cancellationToken);
        await using var command = CreateCommand(script);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    protected async virtual Task<T> RunScript<T>(string script, CancellationToken cancellationToken)
    {
        await OpenConnection(cancellationToken);
        await using var command = CreateCommand(script);
        var ret = await command.ExecuteScalarAsync(cancellationToken);
        if (ret is not T retT)
            throw new InvalidCastException($"Expected return type {typeof(T)} but sql script return null or an invalid type");

        return retT;
    }

    public async virtual Task InitializeMigrations(CancellationToken cancellationToken)
        => await RunScript(this.SqlScripts.InitSql, cancellationToken);

    public async virtual Task<ICollection<DatabaseMigration>> GetMigrations(CancellationToken cancellationToken)
    {
        await OpenConnection(cancellationToken);
        await using var command = CreateCommand(this.SqlScripts.GetMigrationsSql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var ret = new List<DatabaseMigration>();

        while (await reader.ReadAsync(cancellationToken))
        {
            ret.Add(ReadMigration(reader));
        }

        return ret;
    }

    protected virtual DatabaseMigration ReadMigration(DbDataReader reader)
    {
        return new DatabaseMigration(
            reader.GetString(0),
            //note: some databases (ex mysql) return datetime with kind unspecified
            DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc),
            reader.GetString(2),
            reader.GetString(3)
        );
    }

    public async virtual Task AddMigration(DatabaseMigration migration, CancellationToken cancellationToken)
    {
        await OpenConnection(cancellationToken);
        await using var command = CreateCommand(this.SqlScripts.AddMigrationSql);
        command.AddParameter("name", migration.Name)
               .AddParameter("deployed_utc", migration.DateTime)
               .AddParameter("user_name", migration.User)
               .AddParameter("hash", migration.Hash);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async virtual Task RemoveMigration(DatabaseMigration migration, CancellationToken cancellationToken)
    {
        await OpenConnection(cancellationToken);
        await using var command = CreateCommand(this.SqlScripts.RemoveMigrationSql);
        command.AddParameter("name", migration.Name);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async virtual Task ClearMigrations(CancellationToken cancellationToken)
        => await RunScript(this.SqlScripts.ClearMigrationsSql, cancellationToken);

    protected virtual DbCommand CreateCommand(string script, DbConnection? connection = null)
    {
        var command = (connection ?? _connection).CreateCommand();
        command.CommandText = script;
        command.CommandTimeout = this.ScriptTimeout;
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Database {0} Running script: {1}", this.Name, script);

        return command;
    }

    protected async virtual Task OpenConnection(CancellationToken cancellationToken)
    {
        try
        {
            if (_connection.State != ConnectionState.Open)
                await _connection.OpenAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open connection to database");
            throw;
        }
    }

    public async virtual ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }
}