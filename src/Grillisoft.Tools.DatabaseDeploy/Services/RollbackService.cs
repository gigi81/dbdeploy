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

        if (_options.DryRun)
            _logger.LogInformation("Dry run enabled: no script will be run and nothing will be written");

        var branches = await LoadBranches(_options.Path, cancellationToken);
        var steps = branches.GetSteps(branches.GetBranch(this.Branch)).ToArray();
        var strategy = await GetStrategy(steps, cancellationToken);
        var rollbackSteps = strategy.GetRollbackSteps(this.Branch).ToArray();

        _logger.LogInformation("Detected {Count} steps to rollback", rollbackSteps.Length);

        //only the databases that have something to rollback take part in the pre and post scripts
        var rollbackDatabases = rollbackSteps.Select(s => s.Step.Database).Distinct().ToArray();

        await RunHooks(DatabaseHook.PreRollback, rollbackDatabases, branches.Directory, _options.DryRun, cancellationToken);

        _progress.Report(0);
        foreach (var (step, migration) in rollbackSteps)
        {
            await RollbackStep(step, migration, cancellationToken);
            _progress.Report(++count * 100 / steps.Length);
        }
        _progress.Report(100);

        var failed = await TryRunHooks(DatabaseHook.PostRollback, rollbackDatabases, branches.Directory, _options.DryRun, cancellationToken);

        var operation = _options.DryRun ? "Dry run (rollback)" : "Rollback";
        if (failed > 0)
            _logger.LogError("{Operation} completed in {ElapsedTime} but {Count} post rollback scripts failed", operation, stopwatch.Elapsed, failed);
        else
            _logger.LogInformation("{Operation} completed successfully in {ElapsedTime}", operation, stopwatch.Elapsed);

        return failed;
    }

    private async Task RollbackStep(Step step, DatabaseMigration migration, CancellationToken cancellationToken)
    {
        if (_options.DryRun)
        {
            _dbl[step.Database].LogInformation("Dry run: {StepName} would be rolled back", step.Name);
            return;
        }

        var database = await GetDatabase(step.Database, cancellationToken);
        await RunScript(step.RollbackScript, database, cancellationToken);
        _dbl[step.Database].LogInformation("Removing migration {StepName}", step.Name);
        await database.RemoveMigration(migration, cancellationToken);
    }
}