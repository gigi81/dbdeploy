using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy.Abstractions;

public interface IDatabasesCollection
{
    IReadOnlyCollection<string> Databases { get; }
    Task<IDatabase> GetDatabase(string name, CancellationToken cancellationToken);

    /// <summary>
    /// The formatter for a database's dialect, worked out from configuration alone: no
    /// <see cref="IDatabase"/> is built and no connection string is needed, so formatting never
    /// depends on a database being configured or reachable. Returns <c>null</c> when nothing in
    /// the configuration says which provider the database uses.
    /// </summary>
    ISqlFormatter? GetSqlFormatter(string name);

    /// <summary>
    /// The names of the hook scripts of a database, worked out from configuration alone in the
    /// same way as <see cref="GetSqlFormatter"/>: the per database settings override the global
    /// ones, no <see cref="IDatabase"/> is built and no connection is needed. Hooks that are not
    /// configured come back with an empty name.
    /// </summary>
    DatabaseHooks GetHooks(string name);
}