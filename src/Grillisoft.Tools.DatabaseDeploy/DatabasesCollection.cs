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

    public ISqlFormatter? GetSqlFormatter(string name)
    {
        var provider = GetProvider(name);

        if (string.IsNullOrWhiteSpace(provider))
            return null;

        if (!_databaseFactories.TryGetValue(provider, out var factory))
            throw new DatabaseProviderNotFoundException(provider, name);

        return factory.SqlFormatter;
    }

    /// <summary>
    /// The names are merged allowing an empty one: unlike the other settings an empty value here
    /// is not "nothing was said", it is how a database opts out of a hook the global settings
    /// turned on.
    /// </summary>
    public DatabaseHooks GetHooks(string name)
    {
        var section = _configurationSection.GetSection(name);

        return new DatabaseHooks(
            _global.PreDeploy.OverrideWithAllowEmpty(section["preDeploy"]),
            _global.PostDeploy.OverrideWithAllowEmpty(section["postDeploy"]),
            _global.PreRollback.OverrideWithAllowEmpty(section["preRollback"]),
            _global.PostRollback.OverrideWithAllowEmpty(section["postRollback"]));
    }

    private async Task<IDatabase> CreateDatabase(string name, CancellationToken cancellationToken)
    {
        var section = _configurationSection.GetSection(name);
        var provider = GetProvider(name);

        if (string.IsNullOrWhiteSpace(provider) || !_databaseFactories.TryGetValue(provider, out var factory))
            throw new DatabaseProviderNotFoundException(provider, name);

        var database = await factory.GetDatabase(name, section, cancellationToken);
        if (database == null)
            throw new DatabaseConfigNotFoundException(name);

        return database;
    }

    private string GetProvider(string name) =>
        _global.DefaultProvider.OverrideWith(_configurationSection.GetSection(name)["provider"]);

    public async ValueTask DisposeAsync()
    {
        foreach (var database in _databases.Values)
        {
            await database.DisposeAsync();
        }

        _databases.Clear();
    }
}