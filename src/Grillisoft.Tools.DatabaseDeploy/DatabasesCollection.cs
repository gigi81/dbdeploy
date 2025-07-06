using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Exceptions;
using Microsoft.Extensions.Configuration;

namespace Grillisoft.Tools.DatabaseDeploy;

public class DatabasesCollection : IDatabasesCollection, IAsyncDisposable
{
    private readonly Dictionary<string, IDatabaseFactory> _databaseFactories;
    private readonly Dictionary<string, IDatabase> _databases = new(StringComparer.InvariantCultureIgnoreCase);
    private readonly IConfigurationSection _configurationSection;
    private readonly GlobalSettings _global;
    private readonly Lazy<List<string>> _keys;

    public DatabasesCollection(IEnumerable<IDatabaseFactory> databaseFactories, IConfiguration configuration)
    {
        _databaseFactories = databaseFactories.ToDictionary(f => f.Name, f => f, StringComparer.InvariantCultureIgnoreCase);
        _configurationSection = configuration.GetSection("databases");
        _global = configuration.GetSection(GlobalSettings.SectionName)?.Get<GlobalSettings>() ?? new GlobalSettings();
        _keys = new Lazy<List<string>>(() => _configurationSection.GetChildren().Select(c => c.Key).ToList());
    }

    public IReadOnlyCollection<string> Databases => _keys.Value;

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
        var provider = _global.DefaultProvider.OverrideWith(section["provider"]);

        if (string.IsNullOrWhiteSpace(provider) || !_databaseFactories.TryGetValue(provider, out var factory))
            throw new DatabaseProviderNotFoundException(provider, name);

        var database = await factory.GetDatabase(name, section, cancellationToken);
        if (database == null)
            throw new DatabaseConfigNotFoundException(name);

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