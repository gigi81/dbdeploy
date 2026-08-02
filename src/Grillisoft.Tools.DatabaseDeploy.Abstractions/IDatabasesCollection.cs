namespace Grillisoft.Tools.DatabaseDeploy.Abstractions;

public interface IDatabasesCollection
{
    /// <summary>
    /// List of all databases names
    /// </summary>
    IReadOnlyCollection<string> Databases { get; }
    
    /// <summary>
    /// Gets an instance of <see cref="IDatabase"/> for the specified database
    /// </summary>
    /// <param name="name">Database name</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<IDatabase> GetDatabase(string name, CancellationToken cancellationToken);

    /// <summary>
    /// The formatter for a database's dialect, worked out from configuration alone: no
    /// <see cref="IDatabase"/> is built and no connection string is needed, so formatting never
    /// depends on a database being configured or reachable. Returns <c>null</c> when nothing in
    /// the configuration says which provider the database uses.
    /// </summary>
    ISqlFormatter? GetSqlFormatter(string name);

    /// <summary>
    /// The hook scripts of a database, worked out from configuration alone in the same way as
    /// <see cref="GetSqlFormatter"/>: the per database settings override the global ones, no
    /// <see cref="IDatabase"/> is built and no connection is needed. A hook that is not configured
    /// simply has no script.
    /// </summary>
    /// <param name="name">Database name</param>
    IDatabaseHooks GetHooks(string name);
}