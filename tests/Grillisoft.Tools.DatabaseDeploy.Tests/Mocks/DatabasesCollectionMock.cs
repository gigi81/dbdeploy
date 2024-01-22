using Grillisoft.Tools.DatabaseDeploy.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.Tests.Mocks;

public class DatabasesCollectionMock : IDatabasesCollection
{
    private readonly Dictionary<string,IDatabase> _databases;

    public DatabasesCollectionMock(params IDatabase[] databases)
    {
        _databases = databases.ToDictionary(d => d.Name, d => d, StringComparer.InvariantCultureIgnoreCase);
    }
    
    public Task<IDatabase> GetDatabase(string name, CancellationToken cancellationToken)
    {
        if(!string.IsNullOrWhiteSpace(name) && _databases.TryGetValue(name, out var ret))
            return Task.FromResult(ret);

        throw new Exception($"Mock database {name} not found");
    }
}