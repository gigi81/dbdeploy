using Microsoft.Extensions.Configuration;

namespace Grillisoft.Tools.DatabaseDeploy.Abstractions;

public interface IDatabaseFactory
{
    string Name { get; }

    /// <summary>
    /// The formatter for this provider's dialect. It is exposed here as well as on
    /// <see cref="IDatabase"/> so that formatting a loose folder of scripts needs neither a
    /// connection string nor a configured database.
    /// </summary>
    ISqlFormatter SqlFormatter { get; }

    Task<IDatabase> GetDatabase(string name, IConfigurationSection config, CancellationToken cancellationToken);
}
