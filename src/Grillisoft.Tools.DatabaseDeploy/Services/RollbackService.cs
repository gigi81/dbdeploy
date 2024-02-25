using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
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
        IDatabasesCollection databases,
        IFileSystem fileSystem,
        IProgress<int> progress,
        ILogger<RollbackService> logger
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
        
        var steps = branch.Steps.Reverse().ToArray();
        var strategy = await GetStrategy(steps, stoppingToken);
        var rollbackSteps = strategy.GetRollbackSteps().ToArray();
        var count = 0;
        
        _logger.LogInformation("Detected {0} steps to rollback", rollbackSteps.Length);
        _progress.Report(0);
        foreach (var (step, migration) in rollbackSteps)
        {
            var database = await GetDatabase(step.Database, stoppingToken);
            await RunScript(step.RollbackScript, database, stoppingToken);
            await database.RemoveMigration(migration, stoppingToken);
            _progress.Report(++count * 100 / steps.Length);
        }
        _progress.Report(100);
    }
}