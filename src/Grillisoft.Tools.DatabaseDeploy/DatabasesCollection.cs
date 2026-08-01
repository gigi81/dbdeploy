using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Exceptions;

namespace Grillisoft.Tools.DatabaseDeploy;

/// <summary>
/// Builds the databases the settings describe and keeps them for the run. What the settings say is
/// <see cref="DatabasesConfiguration"/>'s job: this one turns it into <see cref="IDatabase"/>.
/// </summary>
public class DatabasesCollection : IDatabasesCollection, IAsyncDisposable
{
    private readonly Dictionary<string, IDatabaseFactory> _databaseFactories;
    private readonly Dictionary<string, IDatabase> _databases = new(StringComparer.InvariantCultureIgnoreCase);
    private readonly DatabasesConfiguration _configuration;

    public DatabasesCollection(IEnumerable<IDatabaseFactory> databaseFactories, DatabasesConfiguration configuration)
    {
        _databaseFactories = databaseFactories.ToDictionary(f => f.Name, f => f, StringComparer.InvariantCultureIgnoreCase);
        _configuration = configuration;
    }

    public IReadOnlyCollection<string> Databases => _configuration.Names;

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
        var provider = _configuration.GetProvider(name);
        if (string.IsNullOrWhiteSpace(provider))
            return null;

        return GetFactory(provider, name).SqlFormatter;
    }

    public DatabaseHooks GetHooks(string name) => _configuration.GetHooks(name);

    private async Task<IDatabase> CreateDatabase(string name, CancellationToken cancellationToken)
    {
        var provider = _configuration.GetProvider(name);

        if (string.IsNullOrWhiteSpace(provider))
            throw new DatabaseProviderNotFoundException(provider, name);

        var factory = GetFactory(provider, name);
        var database = await factory.GetDatabase(name, _configuration.GetSection(name), cancellationToken);
        if (database == null)
            throw new DatabaseConfigNotFoundException(name);

        return database;
    }

    private IDatabaseFactory GetFactory(string provider, string name)
    {
        if (!_databaseFactories.TryGetValue(provider, out var factory))
            throw new DatabaseProviderNotFoundException(provider, name);

        return factory;
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
