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
    private readonly ISqlScripts _sqlScripts;

    protected DatabaseBase(
        string name,
        DbConnection connection,
        ISqlScripts sqlScripts,
        IScriptParser parser,
        ILogger logger)
    {
        _name = name;
        _connection = connection;
        _sqlScripts = sqlScripts;
        _parser = parser;
        _logger = logger;
    }
    
    public string Name => _name;
    
    public IScriptParser ScriptParser => _parser;

    public async virtual Task RunScript(string script, CancellationToken cancellationToken)
    {
        await OpenConnection(cancellationToken);
        await using var command = CreateCommand(script);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async virtual Task InitializeMigrations(CancellationToken cancellationToken)
        => await RunScript(_sqlScripts.InitSql, cancellationToken);

    public async virtual Task<ICollection<DatabaseMigration>> GetMigrations(CancellationToken cancellationToken)
    {
        await OpenConnection(cancellationToken);
        await using var command = CreateCommand(_sqlScripts.GetSql);
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
        await using var command = CreateCommand(_sqlScripts.AddSql);
        command.AddParameter("name", migration.Name)
               .AddParameter("deployed_utc", migration.DateTime.UtcDateTime)
               .AddParameter("user", migration.User)
               .AddParameter("hash", migration.Hash);
        
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async virtual Task RemoveMigration(DatabaseMigration migration, CancellationToken cancellationToken)
    {
        await OpenConnection(cancellationToken);
        await using var command = CreateCommand(_sqlScripts.RemoveSql);
        command.AddParameter("name", migration.Name);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ClearMigrations(CancellationToken cancellationToken)
        => await RunScript(_sqlScripts.ClearSql, cancellationToken);

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

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }
}