using System.IO.Abstractions;
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
        var manager = await LoadBranchesManager(_options.Path);
       
        if (!manager.Branches.TryGetValue(_options.Branch, out var branch))
            throw new BranchNotFoundException(_options.Branch);

        var steps = manager.GetDeploySteps(branch).ToArray();
        var count = 0;

        _progress.Report(0);

        if (await CheckAndCreateDatabases(steps.Select(s => s.Database).Distinct(), stoppingToken) > 0)
            throw new Exception("One or more databases do not exists");
        
        foreach (var step in steps)
        {
            var database = await GetDatabase(step.Database, stoppingToken);
            var migrations = await GetMigrations(step.Database, stoppingToken);
            
            if (migrations.TryDequeue(out var migration))
            {
                if (!migration.Name.EqualsIgnoreCase(step.Name))
                    throw new StepMigrationMismatchException(step, migration);
                
                _logger.LogInformation($"Database {step.Database} Step {step.Name} already deployed");
            }
            else
            {
                await RunScript(step.DeployScript, database, stoppingToken);
                if(_options.Test)
                    await RunScript(step.TestScript, database, stoppingToken);

                _logger.LogInformation($"Database {step.Database} Adding migration {step.Name}");
                var migrationToAdd = new DatabaseMigration(
                    step.Name,
                    DateTimeOffset.UtcNow,
                    Environment.UserName,
                    step.DeployScript.Name);
                
                await database.AddMigration(migrationToAdd, stoppingToken);
            }
            
            _progress.Report(++count * 100 / steps.Length);
        }
        
        _progress.Report(100);
    }

    private async Task<int> CheckAndCreateDatabases(IEnumerable<string> databases, CancellationToken stoppingToken)
    {
        var errors = 0;
        
        foreach (var database in databases)
        {
            if (!await CheckAndCreateDatabase(database, stoppingToken))
                errors++;
        }

        return errors;
    }

    private async Task<bool> CheckAndCreateDatabase(string name, CancellationToken stoppingToken)
    {
        var database = await GetDatabase(name, stoppingToken);
        try
        {
            if (await database.Exists(stoppingToken))
                return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Database {name} failed to check if database exists");
            return false;
        }

        if (!_options.Create)
        {
            _logger.LogError($"Database {name} does not exists");
            return false;
        }

        try
        {
            _logger.LogInformation($"Database {name} does not exists");
            await database.Create(stoppingToken);
            _logger.LogInformation($"Database {name} created successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Database {name} failed to create database");
            return false;
        }
    }
}