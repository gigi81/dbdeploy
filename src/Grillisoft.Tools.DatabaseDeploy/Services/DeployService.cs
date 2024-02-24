using System.Diagnostics;
using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Exceptions;
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
    
    public async override Task Execute(CancellationToken stoppingToken)
    {
        var count = 0;
        var stopwatch = Stopwatch.StartNew();
        var steps = await GetBranchSteps();
        var databases = steps.Select(s => s.Database).Distinct().ToArray();

        await CheckDatabasesExistsOrCreate(databases, stoppingToken);
        await InitializeMigrations(databases, stoppingToken);
        var deploySteps = await ValidateSequenceAndGetDeploySteps(steps, stoppingToken);

        _logger.LogInformation("Detected {0} steps to deploy", deploySteps.Length);
        _progress.Report(0);
        foreach (var step in deploySteps)
        {
            await DeployStep(step, stoppingToken);
            _progress.Report(++count * 100 / steps.Length);
        }
        _progress.Report(100);
        _logger.LogInformation("Deployment completed successfully in {0}", stopwatch.Elapsed);
    }

    private async Task<Step[]> GetBranchSteps()
    {
        var manager = await LoadBranchesManager(_options.Path);
        if (!manager.Branches.TryGetValue(_options.Branch, out var branch))
            throw new BranchNotFoundException(_options.Branch);

        return manager.GetDeploySteps(branch).ToArray();
    }

    private async Task<Step[]> ValidateSequenceAndGetDeploySteps(Step[] steps, CancellationToken stoppingToken)
    {
        var deploySteps = await steps.WhereAsync(CheckIsStepNotDeployed, stoppingToken)
            .ToArrayAsync(stoppingToken);
        return deploySteps;
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

    private async Task<bool> CheckIsStepNotDeployed(Step step, CancellationToken stoppingToken)
    {
        var migrations = await GetMigrations(step.Database, stoppingToken);
        if (!migrations.TryDequeue(out var migration))
            return true;

        if (!migration.Name.EqualsIgnoreCase(step.Name))
            throw new StepMigrationMismatchException(step, migration);
            
        //TODO: check hash and log warning if different
        _logger.LogInformation($"Database {step.Database} Step {step.Name} already deployed");
        return false;
    }

    private async Task DeployStep(Step step, CancellationToken stoppingToken)
    {
        _logger.LogInformation($"Database {step.Database} Deploying {step.Name}");
        var database = await GetDatabase(step.Database, stoppingToken);
        var hash = await GetHash(step.DeployScript);
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

    private static async Task<string> GetHash(IFileInfo file)
    {
        using var md5 = MD5.Create();
        await using var stream = file.OpenRead();
        var data = await md5.ComputeHashAsync(stream);
        var builder = new StringBuilder(32);

        foreach (var b in data)
        {
            builder.Append(b.ToString("x2")); // Convert to hexadecimal
        }

        return builder.ToString();
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