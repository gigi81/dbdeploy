using Grillisoft.Tools.DatabaseDeploy.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.Tests.Mocks;

public class DatabaseFactoryMock : IDatabaseFactory
{
    private readonly Dictionary<string, IDatabase> _databases = new();

    public DatabaseFactoryMock()
    {
    }

    public DatabaseFactoryMock(string name, IDatabase database)
        : this()
    {
        this.AddDatabase(name, database);
    }
    
    public void AddDatabase(string name, IDatabase database)
    {
        _databases.Add(name, database);
    }
    
    public Task<IDatabase?> GetDatabase(string name, CancellationToken cancellationToken)
    {
        _databases.TryGetValue(name, out var ret);
        return Task.FromResult(ret);
    }
}