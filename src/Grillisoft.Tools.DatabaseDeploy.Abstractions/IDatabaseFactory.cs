using Microsoft.Extensions.Configuration;

namespace Grillisoft.Tools.DatabaseDeploy.Abstractions;

public interface IDatabaseFactory
{
    string Name { get; }
    Task<IDatabase> GetDatabase(string name, IConfigurationSection config, CancellationToken cancellationToken);
}
