using System.IO.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy.Abstractions;

public interface IDatabase : IAsyncDisposable
{
    string Name { get; }
    IScriptParser ScriptParser { get; }
    Task RunScript(string script, CancellationToken cancellationToken);
    Task InitializeMigrations(CancellationToken cancellationToken);
    Task<ICollection<DatabaseMigration>> GetMigrations(CancellationToken cancellationToken);
    Task AddMigration(DatabaseMigration migration, CancellationToken cancellationToken);
    Task RemoveMigration(DatabaseMigration migration, CancellationToken cancellationToken);
    Task ClearMigrations(CancellationToken cancellationToken);
}