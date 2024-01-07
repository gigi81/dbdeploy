using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer;

internal class SqlServerDatabase : IDatabase
{
    private readonly SqlServerScriptParser _parser;

    public SqlServerDatabase(SqlServerScriptParser parser)
    {
        _parser = parser;
    }

    public IScriptParser ScriptParser => _parser;
    public Task RunScript(string script, CancellationToken cancellationToken) => throw new NotImplementedException();

    public Task InitializeMigrations(CancellationToken cancellationToken) => throw new NotImplementedException();

    public Task<DatabaseMigration[]> GetMigrations(CancellationToken cancellationToken) => throw new NotImplementedException();

    public Task AddMigration(DatabaseMigration migration, CancellationToken cancellationToken) => throw new NotImplementedException();

    public Task RemoveMigration(DatabaseMigration migration, CancellationToken cancellationToken) => throw new NotImplementedException();
}