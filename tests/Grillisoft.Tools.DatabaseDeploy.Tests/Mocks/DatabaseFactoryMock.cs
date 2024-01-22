using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Microsoft.Extensions.Configuration;

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

    public Task<IDatabase> GetDatabase(IConfigurationSection config, CancellationToken cancellationToken)
    {
        var name = config["name"];
        if(!string.IsNullOrWhiteSpace(name) && _databases.TryGetValue(name, out var ret))
            return Task.FromResult(ret);

        throw new Exception($"Mock database {name} not found");
    }
}