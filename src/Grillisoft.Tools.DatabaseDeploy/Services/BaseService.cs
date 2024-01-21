using System.Diagnostics;
using System.IO.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.Services;

public abstract class BaseService : IExecutable, IAsyncDisposable
{
    private readonly IFileSystem _fileSystem;
    private readonly Dictionary<string, IDatabaseFactory> _databaseFactories;
    private readonly Dictionary<string, DatabaseConfig> _databaseConfigs;
    private readonly Dictionary<string, DatabaseInfo> _databases = new(StringComparer.InvariantCultureIgnoreCase);
    private readonly bool _reverse = false;
    protected readonly ILogger _logger;

    protected BaseService(bool reverse, IEnumerable<DatabaseConfig> databases, IFileSystem fileSystem, IEnumerable<IDatabaseFactory> databaseFactories, ILogger logger)
    {
        _reverse = reverse;
        _databaseConfigs = databases.Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .ToDictionary(c => c.Name!, c => c, StringComparer.InvariantCultureIgnoreCase);
        _fileSystem = fileSystem;
        _databaseFactories = databaseFactories.ToDictionary(f => f.Name, f => f, StringComparer.InvariantCultureIgnoreCase);
        _logger = logger;
    }

    public abstract Task Execute(CancellationToken cancellationToken);

    protected async Task<BranchesManager> LoadBranchesManager(string path)
    {
        var manager = new BranchesManager(_fileSystem.DirectoryInfo.New(path));
        var errors = await manager.Load();

        foreach (var error in errors)
            _logger.LogError(error);

        if (errors.Count > 0)
            throw new Exception("Detected error(s) in branches configuration");

        return manager;
    }

    protected async Task RunScript(IFileInfo scriptFile, IDatabase database, CancellationToken cancellationToken)
    {
        _logger.LogInformation($"Database {database.Name} Running script {scriptFile.FullName}");
        await foreach (var script in database.ScriptParser.Parse(scriptFile, cancellationToken))
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();
                await database.RunScript(script, cancellationToken);
                _logger.LogInformation($"Database {database.Name} Script {scriptFile.FullName} executed in {stopwatch.Elapsed}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Database {database.Name} Failed to run script {{0}}", script);
                throw;
            }
        }
    }

    protected async Task<DatabaseInfo> GetDatabase(string name, CancellationToken cancellationToken)
    {
        if (_databases.TryGetValue(name, out var ret))
            return ret;

        ret = await CreateDatabase(name, cancellationToken);
        _databases.Add(name, ret);
        return ret;
    }
    
    protected async Task<DatabaseInfo> CreateDatabase(string name, CancellationToken cancellationToken)
    {
        if (!_databaseConfigs.TryGetValue(name, out var config))
            throw new Exception($"Could not find configuration for database '{name}'");

        if (string.IsNullOrWhiteSpace(config.Provider) || !_databaseFactories.TryGetValue(config.Provider, out var factory))
            throw new Exception($"Could not find factory '{config.Provider}' for database '{name}'");

        var database = await factory.GetDatabase(config, cancellationToken);
        if (database == null)
            throw new Exception($"Database '{name}' not found");

        IEnumerable<DatabaseMigration> migrations = await database.GetMigrations(cancellationToken);
        if (_reverse)
            migrations = migrations.Reverse();
        
        return new(name, database, migrations.ToQueue());
    }
    
    protected record DatabaseInfo(string Name, IDatabase Database, Queue<DatabaseMigration> Migrations);

    public async ValueTask DisposeAsync()
    {
        foreach (var database in _databases.Values)
        {
            await database.Database.DisposeAsync();
        }
        
        _databases.Clear();
    }
}