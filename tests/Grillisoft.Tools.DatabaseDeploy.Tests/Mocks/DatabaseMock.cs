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

    public string Dialect => "ANSI SQL";

    public DatabaseMock(string name, IScriptParser scriptParser)
        : this(name, scriptParser, new SqlFormatterMock())
    {
    }

    public DatabaseMock(string name, IScriptParser scriptParser, ISqlFormatter sqlFormatter)
    {
        this.Name = name;
        this.ScriptParser = scriptParser;
        this.SqlFormatter = sqlFormatter;
    }

    public string Name { get; }

    public ISqlFormatter SqlFormatter { get; }

    /// <summary>
    /// Set to false to test what happens against a database that is not there yet.
    /// </summary>
    public bool IsExisting { get; set; } = true;

    public bool IsCreated { get; private set; }

    public bool IsMigrationsTableInitialized { get; private set; }

    public Task<bool> Exists(CancellationToken cancellationToken) => Task.FromResult(this.IsExisting);

    public Task Create(CancellationToken cancellationToken)
    {
        this.IsCreated = true;
        this.IsExisting = true;
        return Task.CompletedTask;
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
        this.IsMigrationsTableInitialized = true;
        return Task.CompletedTask;
    }

    public virtual Task<ICollection<DatabaseMigration>> GetMigrations(CancellationToken cancellationToken)
    {
        return Task.FromResult((ICollection<DatabaseMigration>)_migrations);
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

    public Task ClearMigrations(CancellationToken cancellationToken)
    {
        _migrations.Clear();
        return Task.CompletedTask;
    }

    public Task GenerateSchemaDdl(StreamWriter writer, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}