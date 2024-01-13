using System.Data;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Microsoft.Data.SqlClient;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer;

internal class SqlServerDatabase : IDatabase, IAsyncDisposable
{
    private readonly SqlConnection _connection;
    private readonly string _name;
    private readonly string _migrationTableName;
    private readonly SqlServerScriptParser _parser;
    private readonly string _getSql;
    private readonly string _addSql;
    private readonly string _deleteSql;
    private readonly string _createSql;

    public SqlServerDatabase(string name, string connectionString, string migrationTableName, SqlServerScriptParser parser)
    {
        _name = name;
        _connection = new SqlConnection(connectionString);
        _migrationTableName = migrationTableName;
        _parser = parser;

        _getSql = $"SELECT [name], [deployed_utc], [user], [hash] FROM {migrationTableName}";
        _addSql = $"INSERT INTO {migrationTableName} VALUES(@name, @deployed_utc, @user, @hash)";
        _deleteSql = $"DELETE FROM {migrationTableName} WHERE name = @name";
        _createSql = $@"
            IF OBJECT_ID(N'[{migrationTableName}]', N'U') IS NULL
            CREATE TABLE [{migrationTableName}] (
              [name] NVARCHAR(255),
              [deployed_utc] datetime2,
              [user] NVARCHAR(100),
              [hash] char(32),
              CONSTRAINT [PK_{migrationTableName}] PRIMARY KEY CLUSTERED([name] ASC)
            );
        ";
    }

    public string Name => _name;
    
    public IScriptParser ScriptParser => _parser;

    public async Task RunScript(string script, CancellationToken cancellationToken)
    {
        await OpenConnection(cancellationToken);
        await using var command = CreateCommand(script);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task InitializeMigrations(CancellationToken cancellationToken)
    {
        await OpenConnection(cancellationToken);
        await using var command = CreateCommand(_createSql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ICollection<DatabaseMigration>> GetMigrations(CancellationToken cancellationToken)
    {
        await OpenConnection(cancellationToken);
        await using var command = CreateCommand(_getSql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var ret = new List<DatabaseMigration>();
        
        while (await reader.ReadAsync(cancellationToken))
        {
            ret.Add(new DatabaseMigration
            (
                reader.GetString(0),
                reader.GetDateTimeOffset(1),
                reader.GetString(2),
                reader.GetString(3))
            );
        }

        return ret;
    }

    public async Task AddMigration(DatabaseMigration migration, CancellationToken cancellationToken)
    {
        await OpenConnection(cancellationToken);
        await using var command = CreateCommand(_addSql);
        command.Parameters.AddWithValue("name", migration.Name);
        command.Parameters.AddWithValue("datetime", migration.DateTime);
        command.Parameters.AddWithValue("user", migration.User);
        command.Parameters.AddWithValue("hash", migration.Hash);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RemoveMigration(DatabaseMigration migration, CancellationToken cancellationToken)
    {
        await OpenConnection(cancellationToken);
        await using var command = CreateCommand(_deleteSql);
        command.Parameters.AddWithValue("name", migration.Name);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    
    private SqlCommand CreateCommand(string script)
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