using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy.Tests.Mocks;

public class DatabaseMock : IDatabase
{
    private readonly List<DatabaseMigration> _migrations = new();
    private readonly List<string> _scripts = new();

    public DatabaseMock(string name)
        : this(name, new ScriptParserMock())
    {
    }
    
    public DatabaseMock(string name, IScriptParser scriptParser)
    {
        this.Name = name;
        this.ScriptParser = scriptParser;
    }

    public string Name { get; }
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

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}