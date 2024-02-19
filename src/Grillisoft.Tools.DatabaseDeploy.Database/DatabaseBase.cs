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
    private readonly string _databaseName;
    private ISqlScripts? _sqlScripts;
    
    protected DatabaseBase(
        string name,
        DbConnection connection,
        IScriptParser parser,
        ILogger logger)
    {
        _name = name;
        _connection = connection;
        _databaseName = !string.IsNullOrWhiteSpace(_connection.Database) ? _connection.Database : _name;
        _parser = parser;
        _logger = logger;
    }

    protected abstract ISqlScripts CreateSqlScripts();
    
    protected abstract DbConnection CreateConnectionWithoutDatabase();

    private ISqlScripts SqlScripts => _sqlScripts ??= CreateSqlScripts();
    
    public string Name => _name;
    public string DatabaseName => _databaseName;
    public IScriptParser ScriptParser => _parser;
    protected DbConnection Connection => _connection;
    
    public async Task<bool> Exists(CancellationToken cancellationToken)
    {
        await using var connection = this.CreateConnectionWithoutDatabase();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = this.SqlScripts.ExistsSql;
        var ret = await command.ExecuteScalarAsync(cancellationToken);
        if (ret == null)
            throw new Exception($"Expected return type {typeof(bool)} but sql script return null");

        return (int)ret == 1;
    }

    public async Task Create(CancellationToken cancellationToken)
    {
        await using var connection = this.CreateConnectionWithoutDatabase();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = this.SqlScripts.CreateSql;
        await command.ExecuteNonQueryAsync(cancellationToken);
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
        if (ret == null)
            throw new Exception($"Expected return type {typeof(T)} but sql script return null");

        return (T)ret;
    }

    public async virtual Task InitializeMigrations(CancellationToken cancellationToken)
        => await RunScript(this.SqlScripts.InitSql, cancellationToken);

    public async virtual Task<ICollection<DatabaseMigration>> GetMigrations(CancellationToken cancellationToken)
    {
        await OpenConnection(cancellationToken);
        await using var command = CreateCommand(this.SqlScripts.GetSql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var ret = new List<DatabaseMigration>();
        
        while (await reader.ReadAsync(cancellationToken))
        {
            ret.Add(new DatabaseMigration
            (
                reader.GetString(0),
                reader.GetDateTime(1),
                reader.GetString(2),
                reader.GetString(3))
            );
        }

        return ret;
    }

    public async virtual Task AddMigration(DatabaseMigration migration, CancellationToken cancellationToken)
    {
        await OpenConnection(cancellationToken);
        await using var command = CreateCommand(this.SqlScripts.AddSql);
        command.AddParameter("name", migration.Name)
               .AddParameter("deployed_utc", migration.DateTime.UtcDateTime)
               .AddParameter("user", migration.User)
               .AddParameter("hash", migration.Hash);
        
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async virtual Task RemoveMigration(DatabaseMigration migration, CancellationToken cancellationToken)
    {
        await OpenConnection(cancellationToken);
        await using var command = CreateCommand(this.SqlScripts.RemoveSql);
        command.AddParameter("name", migration.Name);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ClearMigrations(CancellationToken cancellationToken)
        => await RunScript(this.SqlScripts.ClearSql, cancellationToken);

    private DbCommand CreateCommand(string script)
    {
        var command = _connection.CreateCommand();
        command.CommandText = script;
        return command;
    }

    private async Task OpenConnection(CancellationToken cancellationToken)
    {
        try
        {
            if (_connection.State != ConnectionState.Open)
                await _connection.OpenAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to open connection to database '{_name}'");
            throw;
        }
    }

    private async Task OpenConnectionToMaster(CancellationToken cancellationToken)
    {
        try
        {
            if (_connection.State != ConnectionState.Open)
                await _connection.OpenAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to open connection to database '{_name}'");
            throw;
        }
    }
    
    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }
}