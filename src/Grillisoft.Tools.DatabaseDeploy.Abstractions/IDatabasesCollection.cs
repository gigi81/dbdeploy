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
}