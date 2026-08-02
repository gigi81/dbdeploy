using System.IO.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// ReSharper disable InconsistentNaming

namespace Grillisoft.Tools.DatabaseDeploy.Services;

public abstract class BaseService : IExecutable
{
    protected readonly IDatabasesCollection _databases;
    protected readonly IOptions<GlobalSettings> _globalSettings;
    protected readonly ILogger _logger;
    protected readonly IDatabaseLoggerFactory _dbl;
    protected readonly IScriptsRunner _scripts;
    protected readonly IDirectoryInfo _rootDirectory;

    protected BaseService(ServiceDependencies dependencies, ILogger logger)
    {
        _databases = dependencies.Databases;
        _globalSettings = dependencies.GlobalSettings;
        _logger = logger;
        _dbl = dependencies.DatabaseLoggers;
        _scripts = dependencies.Scripts;
        _rootDirectory = dependencies.RootDirectory;
    }

    public abstract Task<int> Execute(CancellationToken cancellationToken);

    protected async Task<BranchesReader> LoadBranches(CancellationToken cancellationToken)
    {
        var branches = new BranchesReader(_rootDirectory, _globalSettings.Value);

        _logger.LogInformation("Loading branches from {Directory}", _rootDirectory.FullName);
        await branches.Load();

        var errors = await LayoutValidator.Validate(branches, _globalSettings.Value, _databases);

        foreach (var error in errors)
            _logger.LogError(error);

        if (errors.Count > 0)
            throw new InvalidBranchesConfigurationException(errors);

        return branches;
    }

    protected async Task<Strategy> GetStrategy(Step[] steps, CancellationToken cancellationToken)
    {
        return new Strategy(steps, await GetAllMigrations(steps, cancellationToken), _dbl);
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