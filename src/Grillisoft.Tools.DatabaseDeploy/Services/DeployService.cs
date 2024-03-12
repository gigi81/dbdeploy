using System.Diagnostics;
using System.IO.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Options;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.Services;

public class DeployService : BaseService
{
    private readonly DeployOptions _options;
    private readonly IProgress<int> _progress;

    public DeployService(
        DeployOptions options,
        IDatabasesCollection databases,
        IFileSystem fileSystem,
        IProgress<int> progress,
        ILogger<DeployService> logger
    ) : base(databases, fileSystem, logger)
    {
        _options = options;
        _progress = progress;
    }
    
    public async override Task<int> Execute(CancellationToken stoppingToken)
    {
        var count = 0;
        var stopwatch = Stopwatch.StartNew();
        var steps = await GetBranchSteps(_options.Path, _options.Branch);
        var databases = steps.Select(s => s.Database).Distinct().ToArray();

        await CheckDatabasesExistsOrCreate(databases, stoppingToken);
        await InitializeMigrations(databases, stoppingToken);
        
        var strategy = await GetStrategy(steps, stoppingToken);
        var deploySteps = await strategy.GetDeploySteps(_options.Branch).ToArrayAsync(stoppingToken);

        _logger.LogInformation("Detected {0} steps to deploy", deploySteps.Length);
        _progress.Report(0);
        foreach (var step in deploySteps)
        {
            await DeployStep(step, stoppingToken);
            _progress.Report(++count * 100 / steps.Length);
        }
        _progress.Report(100);
        _logger.LogInformation("Deployment completed successfully in {0}", stopwatch.Elapsed);
        return 0;
    }
    
    private async Task CheckDatabasesExistsOrCreate(string[] databases, CancellationToken stoppingToken)
    {
        var missingDatabases = await databases.WhereAsync(CheckDatabaseIsMissing, stoppingToken)
            .ToArrayAsync(stoppingToken);
        if (missingDatabases.Length > 0)
            throw new Exception("One or more database do not exists");
    }

    private async Task InitializeMigrations(IEnumerable<string> databases, CancellationToken stoppingToken)
    {
        foreach (var database in databases)
        {
            _logger.LogInformation($"Database {database} Initializing");
            var db = await GetDatabase(database, stoppingToken);
            await db.InitializeMigrations(stoppingToken);
        }
    }
    
    private async Task DeployStep(Step step, CancellationToken stoppingToken)
    {
        _logger.LogInformation($"Database {step.Database} Deploying {step.Name}");
        var database = await GetDatabase(step.Database, stoppingToken);
        var hash = await step.GetStepHash();
        await RunScript(step.DeployScript, database, stoppingToken);
        if(_options.Test)
            await RunScript(step.TestScript, database, stoppingToken);

        _logger.LogInformation($"Database {step.Database} Adding migration {step.Name}");
        var migration = new DatabaseMigration(
            step.Name,
            DateTimeOffset.UtcNow,
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
            _logger.LogError(ex, $"Database {name} failed to check if database exists");
            return true;
        }

        if (!_options.Create)
        {
            _logger.LogError($"Database {name} does not exists");
            return true;
        }

        try
        {
            _logger.LogInformation($"Database {name} does not exists");
            await database.Create(stoppingToken);
            _logger.LogInformation($"Database {name} created successfully");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Database {name} failed to create database");
            return true;
        }
    }
}