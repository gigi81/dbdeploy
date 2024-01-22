using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Grillisoft.Tools.DatabaseDeploy;

public class DatabasesCollection : IDatabasesCollection, IAsyncDisposable
{
    private readonly Dictionary<string, IDatabaseFactory> _databaseFactories;
    private readonly Dictionary<string, IDatabase> _databases = new(StringComparer.InvariantCultureIgnoreCase);
    private readonly IConfigurationSection _configurationSection;

    public DatabasesCollection(IEnumerable<IDatabaseFactory> databaseFactories, IConfiguration configuration)
    {
        _databaseFactories = databaseFactories.ToDictionary(f => f.Name, f => f, StringComparer.InvariantCultureIgnoreCase);
        _configurationSection = configuration.GetSection("databases");
    }
    
    public async Task<IDatabase> GetDatabase(string name, CancellationToken cancellationToken)
    {
        if (_databases.TryGetValue(name, out var ret))
            return ret;

        ret = await CreateDatabase(name, cancellationToken);
        _databases.Add(name, ret);
        return ret;
    }
    
    private async Task<IDatabase> CreateDatabase(string name, CancellationToken cancellationToken)
    {
        var section = _configurationSection.GetSection(name);
        var provider = section["provider"];

        if (string.IsNullOrWhiteSpace(provider) || !_databaseFactories.TryGetValue(provider, out var factory))
            throw new Exception($"Could not find factory '{provider}' for database '{name}'");

        var database = await factory.GetDatabase(section, cancellationToken);
        if (database == null)
            throw new Exception($"Database '{name}' not found");

        return database;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var database in _databases.Values)
        {
            await database.DisposeAsync();
        }
        
        _databases.Clear();
    }
}