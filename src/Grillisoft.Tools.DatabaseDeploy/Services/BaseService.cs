using System.Diagnostics;
using System.IO.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.Services;

public abstract class BaseService : IExecutable
{
    private readonly IDatabasesCollection _databases;
    private readonly IFileSystem _fileSystem;
    protected readonly ILogger _logger;

    protected BaseService(IDatabasesCollection databases, IFileSystem fileSystem, ILogger logger)
    {
        _databases = databases;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public abstract Task Execute(CancellationToken cancellationToken);

    protected async Task<BranchesManager> LoadBranchesManager(string path)
    {
        var directory = _fileSystem.DirectoryInfo.New(path);
        var manager = new BranchesManager(directory);
        
        _logger.LogInformation($"Loading branches from {directory.FullName}");
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
        var stopwatch = Stopwatch.StartNew();
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
        _logger.LogInformation($"Database {database.Name} Script {scriptFile.FullName} executed in {stopwatch.Elapsed}");
    }

    protected async Task<Strategy> GetStrategy(Step[] steps, CancellationToken cancellationToken)
    {
        var tasks = steps.Select(s => s.Database)
            .Distinct()
            .Select(name => GetMigrations(name, cancellationToken))
            .ToArray();

        var tuples = await Task.WhenAll(tasks);
        
        var migrations = tuples.ToDictionary(
            m => m.Item1,
            m => m.Item2
        );

        return new Strategy(steps, migrations, _logger);
    }
    protected async Task<IDatabase> GetDatabase(string name, CancellationToken cancellationToken)
    {
        return await _databases.GetDatabase(name, cancellationToken);
    }

    protected async Task<(string, DatabaseMigration[])> GetMigrations(string name, CancellationToken cancellationToken)
    {
        var database = await _databases.GetDatabase(name, cancellationToken);
        var migrations = await database.GetMigrations(cancellationToken);
        return (name, migrations.ToArray());
    }
}