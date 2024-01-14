using System.IO.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.Services;

public abstract class BaseService : IExecutable, IAsyncDisposable
{
    private readonly IFileSystem _fileSystem;
    private readonly IEnumerable<IDatabaseFactory> _databaseFactories;
    private readonly Dictionary<string, DatabaseInfo> _databases = new(StringComparer.InvariantCultureIgnoreCase);
    private readonly bool _reverse = false;
    protected readonly ILogger _logger;

    protected BaseService(bool reverse, IFileSystem fileSystem, IEnumerable<IDatabaseFactory> databaseFactories, ILogger logger)
    {
        _reverse = reverse;
        _fileSystem = fileSystem;
        _databaseFactories = databaseFactories;
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
                await database.RunScript(script, cancellationToken);
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
        foreach (var factory in _databaseFactories)
        {
            var database = await factory.GetDatabase(name, cancellationToken);
            if (database != null)
            {
                IEnumerable<DatabaseMigration> migrations = await database.GetMigrations(cancellationToken);
                if (_reverse)
                    migrations = migrations.Reverse();
                
                return new(name, database, migrations.ToQueue());
            }
        }

        throw new Exception($"Database '{name}' not found");
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