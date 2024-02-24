using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy.Abstractions;

public interface IDatabase : IAsyncDisposable
{
    string Name { get; }
    Task<bool> Exists(CancellationToken cancellationToken);
    Task Create(CancellationToken cancellationToken);
    IScriptParser ScriptParser { get; }
    Task RunScript(string script, CancellationToken cancellationToken);
    Task InitializeMigrations(CancellationToken cancellationToken);
    Task<ICollection<DatabaseMigration>> GetMigrations(CancellationToken cancellationToken);
    Task AddMigration(DatabaseMigration migration, CancellationToken cancellationToken);
    Task RemoveMigration(DatabaseMigration migration, CancellationToken cancellationToken);
    Task ClearMigrations(CancellationToken cancellationToken);
}