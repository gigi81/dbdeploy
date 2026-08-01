using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy.Tests.Mocks;

public class DatabasesCollectionMock : IDatabasesCollection
{
    private readonly Dictionary<string, IDatabase> _databases;

    /// <summary>
    /// The hooks of a database. A database that is not in here has none configured.
    /// </summary>
    public Dictionary<string, DatabaseHooks> Hooks { get; } = new(StringComparer.InvariantCultureIgnoreCase);

    public DatabasesCollectionMock(params IDatabase[] databases)
    {
        _databases = databases.ToDictionary(d => d.Name, d => d, StringComparer.InvariantCultureIgnoreCase);
    }

    public IReadOnlyCollection<string> Databases => _databases.Keys;

    public Task<IDatabase> GetDatabase(string name, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(name) && _databases.TryGetValue(name, out var ret))
            return Task.FromResult(ret);

        throw new Exception($"Mock database {name} not found");
    }

    public DatabaseHooks GetHooks(string name)
    {
        return this.Hooks.GetValueOrDefault(name, DatabaseHooks.None);
    }

    public ISqlFormatter? GetSqlFormatter(string name)
    {
        return !string.IsNullOrWhiteSpace(name) && _databases.TryGetValue(name, out var ret)
            ? ret.SqlFormatter
            : null;
    }
}