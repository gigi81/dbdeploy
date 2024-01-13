using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy.Tests.Mocks;

public class DatabaseMock : IDatabase
{
    private readonly List<DatabaseMigration> _migrations = new();
    private readonly List<string> _scripts = new();

    public DatabaseMock()
        : this(new ScriptParserMock())
    {
    }
    
    public DatabaseMock(IScriptParser scriptParser)
    {
        ScriptParser = scriptParser;
    }

    public IScriptParser ScriptParser { get; }

    public IList<string> Scripts => _scripts;

    public Task RunScript(string script, CancellationToken cancellationToken)
    {
        _scripts.Add(script);
        return Task.CompletedTask;
    }

    public Task InitializeMigrations(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task<ICollection<DatabaseMigration>> GetMigrations(CancellationToken cancellationToken)
    {
        return Task.FromResult((ICollection<DatabaseMigration>) _migrations);
    }

    public Task AddMigration(DatabaseMigration migration, CancellationToken cancellationToken)
    {
        _migrations.Add(migration);
        return Task.CompletedTask;
    }

    public Task RemoveMigration(DatabaseMigration migration, CancellationToken cancellationToken)
    {
        _migrations.Remove(migration);
        return Task.CompletedTask;
    }
}