using System.Diagnostics;
using System.IO.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Exceptions;
using Grillisoft.Tools.DatabaseDeploy.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Soenneker.Extensions.Enumerable;

namespace Grillisoft.Tools.DatabaseDeploy.Services;

public class DeployService : BaseService
{
    private readonly DeployOptions _options;
    private readonly IProgress<int> _progress;

    public DeployService(
        DeployOptions options,
        IDatabasesCollection databases,
        IFileSystem fileSystem,
        IOptions<GlobalSettings> globalOptions,
        IProgress<int> progress,
        ILogger<DeployService> logger
    ) : base(databases, fileSystem, globalOptions, logger)
    {
        _options = options;
        _progress = progress;
    }

    private string Branch => !string.IsNullOrWhiteSpace(_options.Branch)
        ? _options.Branch
        : _globalSettings.Value.DefaultBranch;

    public async override Task<int> Execute(CancellationToken cancellationToken)
    {
        var count = 0;
        var stopwatch = Stopwatch.StartNew();
        var steps = await GetBranchSteps(_options.Path, this.Branch, cancellationToken);
        var databases = steps.Select(s => s.Database).Distinct().ToArray();

        await CheckDatabasesExistsOrCreate(databases, cancellationToken);
        await InitializeMigrations(databases, cancellationToken);

        var strategy = await GetStrategy(steps, cancellationToken);
        var deploySteps = await strategy.GetDeploySteps(this.Branch).ToArrayAsync(cancellationToken);

        _logger.LogInformation("Detected {0} steps to deploy", deploySteps.Length);
        _progress.Report(0);
        foreach (var step in deploySteps)
        {
            await DeployStep(step, cancellationToken);
            _progress.Report(++count * 100 / steps.Length);
        }
        _progress.Report(100);
        _logger.LogInformation("Deployment completed successfully in {0}", stopwatch.Elapsed);
        return 0;
    }

    private async Task CheckDatabasesExistsOrCreate(string[] databases, CancellationToken cancellationToken)
    {
        var missingDatabases = await databases.WhereAsync(CheckDatabaseIsMissing, cancellationToken)
            .ToArrayAsync(cancellationToken);
        if (missingDatabases.Length > 0)
            throw new DatabasesNotFoundException(missingDatabases);
    }

    private async Task InitializeMigrations(IEnumerable<string> databases, CancellationToken stoppingToken)
    {
        foreach (var database in databases)
        {
            _dbl[database].LogInformation($"Initializing Migrations");
            var db = await GetDatabase(database, stoppingToken);
            await db.InitializeMigrations(stoppingToken);
        }
    }

    private async Task DeployStep(Step step, CancellationToken stoppingToken)
    {
        _dbl[step.Database].LogInformation("Deploying {StepName}", step.Name);
        var database = await GetDatabase(step.Database, stoppingToken);
        var hash = await step.GetStepHash();
        await RunScript(step.DeployScript, database, stoppingToken);
        await RunScripts(step.DataScripts, database, stoppingToken);
        if (_options.Test)
            await RunScript(step.TestScript, database, stoppingToken);

        _dbl[step.Database].LogInformation("Adding migration {StepName}", step.Name);
        var migration = new DatabaseMigration(
            step.Name,
            Environment.UserName,
            hash);

        await database.AddMigration(migration, stoppingToken);
    }

    private async Task<bool> CheckDatabaseIsMissing(string name, CancellationToken stoppingToken)
    {
        var database = await GetDatabase(name, stoppingToken);
        try
        {
            if (await database.Exists(stoppingToken))
                return false;
        }
        catch (Exception ex)
        {
            _dbl[name].LogError(ex, "Failed to check if database exists");
            return true;
        }

        if (!_options.Create)
        {
            _dbl[name].LogError("Database does not exists or current user does not have permission to access database");
            return true;
        }

        try
        {
            _dbl[name].LogError("Database does not exists. Creating new database");
            await database.Create(stoppingToken);
            _dbl[name].LogInformation("Database created successfully");
            return false;
        }
        catch (Exception ex)
        {
            _dbl[name].LogError(ex, "Failed to create database");
            return true;
        }
    }
}