using System.IO.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Exceptions;
using Grillisoft.Tools.DatabaseDeploy.Options;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.Services;

public class RollbackService : BaseService
{
    private readonly RollbackOptions _options;
    private readonly IFileSystem _fileSystem;

    public RollbackService(
        RollbackOptions options,
        IFileSystem fileSystem,
        IEnumerable<IDatabaseFactory> databaseFactories,
        ILogger<RollbackService> logger
     ) : base(fileSystem, databaseFactories, logger)
    {
        _options = options;
        _fileSystem = fileSystem;
    }

    public async override Task Execute(CancellationToken stoppingToken)
    {
        var manager = await LoadBranchesManager(_options.Path);

        if (!manager.Branches.TryGetValue(_options.Branch, out var branch))
            throw new BranchNotFoundException(_options.Branch);

        var databases = await GetDatabases(branch.Databases, true, stoppingToken);

        foreach (var step in manager.GetRollbackSteps(branch))
        {
            //if we get to the init script, we are done
            if (step.IsInit)
                break;
                
            var (_, database, migrations) = databases[step.Database];
            
            //if there are no more migrations to rollback, we are done
            if (!migrations.TryPeek(out var migration))
                break;

            //step name does not match the migration step
            if (!migration.Name.EqualsIgnoreCase(step.Name))
            {
                _logger.LogInformation($"Database {database.Name} Step {step.Name} was not deployed. Skipping rollback");
                continue;
            }
            
            await RunScript(step.RollbackScript, database, stoppingToken);
            await database.RemoveMigration(migration, stoppingToken);
            migrations.Dequeue();
        }
    }
}