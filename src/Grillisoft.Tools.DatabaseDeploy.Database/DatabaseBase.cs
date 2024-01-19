using System.Data;
using System.Data.Common;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy.Database;

public abstract class DatabaseBase : IDatabase
{
    private readonly string _name;
    private readonly DbConnection _connection;
    private readonly IScriptParser _parser;

    protected DatabaseBase(string name, DbConnection connection, IScriptParser parser)
    {
        _name = name;
        _parser = parser;
        _connection = connection;
    }
    
    protected abstract string InitSql { get; }
    
    protected abstract string GetSql { get; }
    
    protected abstract string AddSql { get; }
    
    protected abstract string RemoveSql { get; }
    
    public string Name => _name;
    
    public IScriptParser ScriptParser => _parser;

    public async virtual Task RunScript(string script, CancellationToken cancellationToken)
    {
        await OpenConnection(cancellationToken);
        await using var command = CreateCommand(script);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async virtual Task InitializeMigrations(CancellationToken cancellationToken)
    {
        await OpenConnection(cancellationToken);
        await using var command = CreateCommand(this.InitSql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async virtual Task<ICollection<DatabaseMigration>> GetMigrations(CancellationToken cancellationToken)
    {
        await OpenConnection(cancellationToken);
        await using var command = CreateCommand(this.GetSql);
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
        await using var command = CreateCommand(this.AddSql);
        command.AddParameter("name", migration.Name)
               .AddParameter("datetime", migration.DateTime)
               .AddParameter("user", migration.User)
               .AddParameter("hash", migration.Hash);
        
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async virtual Task RemoveMigration(DatabaseMigration migration, CancellationToken cancellationToken)
    {
        await OpenConnection(cancellationToken);
        await using var command = CreateCommand(this.RemoveSql);
        command.AddParameter("name", migration.Name);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    
    private DbCommand CreateCommand(string script)
    {
        var command = _connection.CreateCommand();
        command.CommandText = script;
        return command;
    }

    private async Task OpenConnection(CancellationToken cancellationToken)
    {
        if (_connection.State != ConnectionState.Open)
            await _connection.OpenAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }
}