using System.Diagnostics;
using System.IO.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
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

    public async override Task<int> Execute(CancellationToken stoppingToken)
    {
        var count = 0;
        var stopwatch = Stopwatch.StartNew();
        var steps = await GetBranchSteps(_options.Path, _options.Branch);
        var strategy = await GetStrategy(steps, stoppingToken);
        var rollbackSteps = strategy.GetRollbackSteps(_options.Branch).ToArray();
        
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
        _logger.LogInformation("Rollback completed successfully in {0}", stopwatch.Elapsed);
        return 0;
    }
}