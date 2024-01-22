using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Microsoft.Extensions.Configuration;

namespace Grillisoft.Tools.DatabaseDeploy.Abstractions;

public interface IDatabaseFactory
{
    string Name { get; }
    Task<IDatabase> GetDatabase(IConfigurationSection config, CancellationToken cancellationToken);
}
