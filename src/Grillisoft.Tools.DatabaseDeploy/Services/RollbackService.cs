using System.IO.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Exceptions;
using Grillisoft.Tools.DatabaseDeploy.Options;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.Services;

public class RollbackService : BaseService
{
    private readonly RollbackOptions _options;
    private readonly IProgress<int> _progress;

    public RollbackService(
        RollbackOptions options,
        IFileSystem fileSystem,
        IEnumerable<IDatabaseFactory> databaseFactories,
        IProgress<int> progress,
        ILogger<RollbackService> logger
     ) : base(true, fileSystem, databaseFactories, logger)
    {
        _options = options;
        _progress = progress;
    }

    public async override Task Execute(CancellationToken stoppingToken)
    {
        var manager = await LoadBranchesManager(_options.Path);

        if (!manager.Branches.TryGetValue(_options.Branch, out var branch))
            throw new BranchNotFoundException(_options.Branch);
        
        var steps = manager.GetRollbackSteps(branch).ToArray();
        var count = 0;
        
        _progress.Report(0);
        
        foreach (var step in steps)
        {
            //if we get to the init script, we are done
            if (step.IsInit)
                break;
                
            var (_, database, migrations) = await GetDatabase(step.Database, stoppingToken);
            
            //if there are no more migrations to rollback, we are done
            if (!migrations.TryPeek(out var migration))
                break;

            //step name does not match the migration step
            if (!migration.Name.EqualsIgnoreCase(step.Name))
            {
                _logger.LogInformation($"Database {database.Name} Step {step.Name} was not deployed. Skipping rollback");
            }
            else
            {
                await RunScript(step.RollbackScript, database, stoppingToken);
                await database.RemoveMigration(migration, stoppingToken);
                migrations.Dequeue();
            }
            
            _progress.Report(++count * 100 / steps.Length);
        }
        
        _progress.Report(100);
    }
}