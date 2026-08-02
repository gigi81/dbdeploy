using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy.Tests.Mocks;

public class DatabasesCollectionMock : IDatabasesCollection
{
    private readonly Dictionary<string, IDatabase> _databases;
    private readonly IDirectoryInfo _root;

    /// <summary>
    /// The hook script names of a database. A database that is not in here has none configured.
    /// </summary>
    public Dictionary<string, IDictionary<DatabaseHook, string>> Hooks { get; } =
        new(StringComparer.InvariantCultureIgnoreCase);

    /// <param name="root">The folder the hook scripts are looked up in, as the real collection has</param>
    /// <param name="databases">The databases the collection knows about</param>
    public DatabasesCollectionMock(IDirectoryInfo root, params IDatabase[] databases)
    {
        _root = root;
        _databases = databases.ToDictionary(d => d.Name, d => d, StringComparer.InvariantCultureIgnoreCase);
    }

    /// <summary>
    /// For the tests that never look a hook up, and so have no folder to root one at.
    /// </summary>
    public DatabasesCollectionMock(params IDatabase[] databases)
        : this(new MockFileSystem().DirectoryInfo.New(SampleBranches.RootPath), databases)
    {
    }

    public IReadOnlyCollection<string> Databases => _databases.Keys;

    public Task<IDatabase> GetDatabase(string name, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(name) && _databases.TryGetValue(name, out var ret))
            return Task.FromResult(ret);

        throw new Exception($"Mock database {name} not found");
    }

    public IDatabaseHooks GetHooks(string name)
    {
        return new DatabaseHooks(this.Hooks.GetValueOrDefault(name, TestHooks.None), name, _root);
    }

    public ISqlFormatter? GetSqlFormatter(string name)
    {
        return !string.IsNullOrWhiteSpace(name) && _databases.TryGetValue(name, out var ret)
            ? ret.SqlFormatter
            : null;
    }
}