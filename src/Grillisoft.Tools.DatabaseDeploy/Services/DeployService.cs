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
        IFileSystem fileSystem,
        IEnumerable<IDatabaseFactory> databaseFactories,
        IProgress<int> progress,
        ILogger<DeployService> logger
    ) : base(fileSystem, databaseFactories, logger)
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
        var databases = await GetDatabases(steps.Select(s => s.Database).Distinct(), false, stoppingToken);
        var count = 0;

        _progress.Report(0);
        
        foreach (var step in steps)
        {
            var (_, database, migrations) = databases[step.Database];
            
            if (migrations.TryDequeue(out var migration))
            {
                if (!migration.Name.EqualsIgnoreCase(step.Name))
                    throw new StepMigrationMismatchException(step, migration);
                
                _logger.LogInformation($"Database {database.Name} Step {step.Name} already deployed");
            }
            else
            {
                await RunScript(step.DeployScript, database, stoppingToken);
                if(_options.UnitTest)
                    await RunScript(step.TestScript, database, stoppingToken);

                _logger.LogInformation($"Database {database.Name} Adding migration {step.Name}");
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
}