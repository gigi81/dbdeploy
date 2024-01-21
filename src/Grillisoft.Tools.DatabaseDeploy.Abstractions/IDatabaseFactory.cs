using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy.Abstractions;

public interface IDatabaseFactory
{
    string Name { get; }
    Task<IDatabase> GetDatabase(DatabaseConfig config, CancellationToken cancellationToken);
}
