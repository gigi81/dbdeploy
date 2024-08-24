using System.Diagnostics;
using System.IO.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Grillisoft.Tools.DatabaseDeploy.Services;

public class RollbackService : BaseService
{
    private readonly RollbackOptions _options;
    private readonly IProgress<int> _progress;

    public RollbackService(
        RollbackOptions options,
        IDatabasesCollection databases,
        IFileSystem fileSystem,
        IOptions<GlobalSettings> globalOptions,
        IProgress<int> progress,
        ILogger<RollbackService> logger
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
        var steps = await GetBranchSteps(_options.Path, this.Branch);
        var strategy = await GetStrategy(steps, cancellationToken);
        var rollbackSteps = strategy.GetRollbackSteps(this.Branch).ToArray();

        _logger.LogInformation("Detected {0} steps to rollback", rollbackSteps.Length);
        _progress.Report(0);
        foreach (var (step, migration) in rollbackSteps)
        {
            var database = await GetDatabase(step.Database, cancellationToken);
            await RunScript(step.RollbackScript, database, cancellationToken);
            _dbl[step.Database].LogInformation("Removing migration {StepName}", step.Name);
            await database.RemoveMigration(migration, cancellationToken);
            _progress.Report(++count * 100 / steps.Length);
        }
        _progress.Report(100);
        _logger.LogInformation("Rollback completed successfully in {0}", stopwatch.Elapsed);
        return 0;
    }
}