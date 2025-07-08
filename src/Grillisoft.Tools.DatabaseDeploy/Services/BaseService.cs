using System.Diagnostics;
using System.IO.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Soenneker.Extensions.String;

// ReSharper disable InconsistentNaming

namespace Grillisoft.Tools.DatabaseDeploy.Services;

public abstract class BaseService : IExecutable
{
    private readonly IDatabasesCollection _databases;
    private readonly IFileSystem _fileSystem;
    protected readonly IOptions<GlobalSettings> _globalSettings;
    protected readonly ILogger _logger;
    protected readonly DatabaseLoggerFactory _dbl;

    protected BaseService(
        IDatabasesCollection databases,
        IFileSystem fileSystem,
        IOptions<GlobalSettings> globalSettings,
        ILogger logger)
    {
        _databases = databases;
        _fileSystem = fileSystem;
        _globalSettings = globalSettings;
        _logger = logger;
        _dbl = new DatabaseLoggerFactory(logger);
    }

    public abstract Task<int> Execute(CancellationToken cancellationToken);

    protected async Task<Step[]> GetBranchSteps(string path, string branchName, CancellationToken cancellationToken)
    {
        var manager = await LoadBranchesManager(path, cancellationToken);
        if (!manager.Branches.TryGetValue(branchName, out var branch))
            throw new BranchNotFoundException(branchName);

        return manager.GetSteps(branch).ToArray();
    }

    protected IDirectoryInfo GetDirectory(string path)
    {
        return _fileSystem.DirectoryInfo.New(path);
    }

    protected async Task<BranchesManager> LoadBranchesManager(string path, CancellationToken cancellationToken)
    {
        var directory = this.GetDirectory(path);
        var manager = new BranchesManager(directory, _globalSettings.Value);

        _logger.LogInformation("Loading branches from {Directory}", directory.FullName);
        var errors = await manager.Load();

        foreach (var error in errors)
            _logger.LogError(error);

        if (errors.Count > 0)
            throw new InvalidBranchesConfigurationException(errors);

        return manager;
    }

    protected async Task RunScripts(IEnumerable<IFileInfo> scriptFiles, IDatabase database, CancellationToken cancellationToken)
    {
        foreach (var scriptFile in scriptFiles)
        {
            await RunScript(scriptFile, database, cancellationToken);
        }
    }

    protected async Task RunScript(IFileInfo scriptFile, IDatabase database, CancellationToken cancellationToken)
    {
        _dbl[database.Name].LogInformation("Running script {ScriptPath}", scriptFile.FullName);
        var stopwatch = Stopwatch.StartNew();
        await foreach (var script in database.ScriptParser.Parse(scriptFile, cancellationToken))
        {
            try
            {
                await database.RunScript(script, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _dbl[database.Name].LogError(ex, "Failed to run script {ScriptContent}", script.Truncate(20_000));
                throw;
            }
        }
        _dbl[database.Name].LogInformation("Script {ScriptPath} executed in {ExecutionTime}", scriptFile.FullName, stopwatch.Elapsed);
    }

    protected async Task<Strategy> GetStrategy(Step[] steps, CancellationToken cancellationToken)
    {
        return new Strategy(steps, await GetAllMigrations(steps, cancellationToken), _logger);
    }

    private async Task<Dictionary<string, DatabaseMigration[]>> GetAllMigrations(Step[] steps, CancellationToken cancellationToken)
    {
        var tasks = steps.Select(s => s.Database)
            .Distinct()
            .Select(name => GetMigrations(name, cancellationToken))
            .ToArray();

        var tuples = await Task.WhenAll(tasks);

        return tuples.ToDictionary(
            m => m.Item1,
            m => m.Item2
        );
    }

    protected IReadOnlyCollection<string> Databases => _databases.Databases;

    protected async Task<IDatabase> GetDatabase(string name, CancellationToken cancellationToken)
    {
        return await _databases.GetDatabase(name, cancellationToken);
    }

    private async Task<(string, DatabaseMigration[])> GetMigrations(string name, CancellationToken cancellationToken)
    {
        var database = await _databases.GetDatabase(name, cancellationToken);
        var migrations = await database.GetMigrations(cancellationToken);
        _dbl[database.Name].LogInformation("Found {MigrationsCount} existing migrations in database", migrations.Count);
        return (name, migrations.ToArray());
    }
}