using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy.Tests.Mocks;

public class DatabaseFactoryMock : IDatabaseFactory
{
    private readonly Dictionary<string, IDatabase> _databases = new();

    public string Name => "mock";
    
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

    public Task<IDatabase> GetDatabase(DatabaseConfig config, CancellationToken cancellationToken)
    {
        if(_databases.TryGetValue(config.Name!, out var ret))
            return Task.FromResult(ret);

        throw new Exception($"Mock database {config.Name} not found");
    }
}