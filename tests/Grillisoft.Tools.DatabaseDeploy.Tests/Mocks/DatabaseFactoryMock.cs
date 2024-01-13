using Grillisoft.Tools.DatabaseDeploy.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.Tests.Mocks;

public class DatabaseFactoryMock : IDatabaseFactory
{
    private readonly Dictionary<string, IDatabase> _databases = new();

    public DatabaseFactoryMock()
    {
    }

    public DatabaseFactoryMock(params IDatabase[] databases)
        : this()
    {
        foreach (var database in databases)
        {
            _databases.Add(database.Name, database);
        }
    }
    
    public Task<IDatabase?> GetDatabase(string name, CancellationToken cancellationToken)
    {
        _databases.TryGetValue(name, out var ret);
        return Task.FromResult(ret);
    }
}