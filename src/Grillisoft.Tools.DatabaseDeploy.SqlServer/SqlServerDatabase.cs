using System.Data;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Microsoft.Data.SqlClient;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer;

internal class SqlServerDatabase : IDatabase, IAsyncDisposable
{
    private readonly SqlConnection _connection;
    private readonly SqlServerScriptParser _parser;

    public SqlServerDatabase(string connectionString, SqlServerScriptParser parser)
    {
        _connection = new SqlConnection(connectionString);
        _parser = parser;
    }

    public IScriptParser ScriptParser => _parser;

    public async Task RunScript(string script, CancellationToken cancellationToken)
    {
        await OpenConnection(cancellationToken);
        await using var command = _connection.CreateCommand();
        command.CommandText = script;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task InitializeMigrations(CancellationToken cancellationToken) => throw new NotImplementedException();

    public Task<DatabaseMigration[]> GetMigrations(CancellationToken cancellationToken) => throw new NotImplementedException();

    public Task AddMigration(DatabaseMigration migration, CancellationToken cancellationToken) => throw new NotImplementedException();

    public Task RemoveMigration(DatabaseMigration migration, CancellationToken cancellationToken) => throw new NotImplementedException();

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