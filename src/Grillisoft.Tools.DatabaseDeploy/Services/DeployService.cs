using System.Diagnostics;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Exceptions;
using Grillisoft.Tools.DatabaseDeploy.Options;
using Microsoft.Extensions.Logging;
using Soenneker.Extensions.Enumerable;
using Soenneker.Extensions.String;

namespace Grillisoft.Tools.DatabaseDeploy.Services;

public class DeployService : BranchService
{
    private readonly DeployOptions _options;
    private readonly IProgress<int> _progress;

    public DeployService(
        DeployOptions options,
        ServiceDependencies dependencies,
        IProgress<int> progress,
        ILogger<DeployService> logger
    ) : base(options, dependencies, logger)
    {
        _options = options;
        _progress = progress;
    }

    public async override Task<int> Execute(CancellationToken cancellationToken)
    {
        var count = 0;
        var stopwatch = Stopwatch.StartNew();

        if (_options.DryRun)
            _logger.LogInformation("Dry run enabled: no script will be run and nothing will be written");

        var branches = await LoadBranches(cancellationToken);
        var branch = branches.GetBranch(this.Branch);
        var steps = branches.GetSteps(branch).ToArray();
        var databases = steps.Select(s => s.Database).Distinct().ToArray();

        await CheckDatabasesExistsOrCreate(databases, cancellationToken);
        await InitializeMigrations(databases, cancellationToken);

        var strategy = await GetStrategy(steps, cancellationToken);
        var deploySteps = await strategy.GetDeploySteps(this.Branch).ToArrayAsync(cancellationToken);
        _logger.LogInformation("Detected {Count} steps to deploy", deploySteps.Length);
        if (deploySteps.Length <= 0)
            return 0;

        //only the databases that have something to deploy take part in the pre and post scripts
        var deployDatabases = deploySteps.Select(s => s.Database).Distinct().ToArray();

        await this.Hooks.Run(DatabaseHook.PreDeploy, deployDatabases, cancellationToken);

        _progress.Report(0);
        foreach (var step in deploySteps)
        {
            await DeployStep(step, cancellationToken);
            _progress.Report(++count * 100 / steps.Length);
        }
        _progress.Report(100);

        var failed = await this.Hooks.TryRun(DatabaseHook.PostDeploy, deployDatabases, cancellationToken);

        var operation = _options.DryRun ? "Dry run (deploy)" : "Deployment";
        if (failed > 0)
            _logger.LogError("{Operation} completed in {ElapsedTime} but {Count} post deploy scripts failed", operation, stopwatch.Elapsed, failed);
        else
            _logger.LogInformation("{Operation} completed successfully in {ElapsedTime}", operation, stopwatch.Elapsed);

        //a post deploy script that failed does not undo the deployment: whatever is left to do is
        //still done, and the failure is reported through the exit code
        if (_options is { Update: true, DryRun: false })
            await UpdateBranches(branches, branch, cancellationToken);

        return failed;
    }

    private async Task UpdateBranches(BranchesReader branches, Branch branch, CancellationToken cancellationToken)
    {
        if (_options.DryRun)
            return;

        var defaultBranch = _globalSettings.Value.DefaultBranch;

        if (branch.Name.EqualsIgnoreCase(defaultBranch))
        {
            _logger.LogInformation("Branch {Branch} is the default branch, nothing to update", branch.Name);
            return;
        }

        //the writer only comes into existence once the run is known to be one that writes
        var writer = new BranchesWriter(branches.Directory, _globalSettings.Value);
        var released = await writer.Release(branch.Steps, branches.GetBranchFiles(branch), cancellationToken);

        _logger.LogInformation(
            "Moved the scripts of branch {ReleasedBranches} to {DefaultBranch}",
            string.Join(", ", released),
            defaultBranch);
    }

    private async Task CheckDatabasesExistsOrCreate(string[] databases, CancellationToken cancellationToken)
    {
        var missingDatabases = await databases.WhereAsync(CheckDatabaseIsMissing, cancellationToken)
            .ToArrayAsync(cancellationToken);
        if (missingDatabases.Length > 0)
            throw new DatabasesNotFoundException(missingDatabases);
    }

    private async Task InitializeMigrations(IEnumerable<string> databases, CancellationToken stoppingToken)
    {
        if (_options.DryRun)
        {
            //creating the migrations table is a change like any other
            _logger.LogInformation("Dry run: the migrations table is not initialized");
            return;
        }

        foreach (var database in databases)
        {
            _dbl[database].LogInformation($"Initializing Migrations");
            var db = await GetDatabase(database, stoppingToken);
            await db.InitializeMigrations(stoppingToken);
        }
    }

    private async Task DeployStep(Step step, CancellationToken stoppingToken)
    {
        if (_options.DryRun)
        {
            _dbl[step.Database].LogInformation("Dry run: {StepName} would be deployed", step.Name);
            return;
        }

        _dbl[step.Database].LogInformation("Deploying {StepName}", step.Name);
        var database = await GetDatabase(step.Database, stoppingToken);
        var hash = await step.GetStepHash();
        await _scripts.Run(step.DeployScript, database, stoppingToken);
        await _scripts.Run(step.DataScripts, database, stoppingToken);
        if (_options.Test)
            await _scripts.Run(step.TestScript, database, stoppingToken);

        _dbl[step.Database].LogInformation("Adding migration {StepName}", step.Name);
        var migration = new DatabaseMigration(
            step.Name,
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
            _dbl[name].LogError(ex, "Failed to check if database exists");
            return true;
        }

        if (!_options.Create)
        {
            _dbl[name].LogError("Database does not exists or current user does not have permission to access database");
            return true;
        }

        if (_options.DryRun)
        {
            //without the database there is nothing to compare the branch against, so the run stops
            //here rather than creating it
            _dbl[name].LogError("Database does not exists. Dry run: the database would have been created");
            return true;
        }

        try
        {
            _dbl[name].LogError("Database does not exists. Creating new database");
            await database.Create(stoppingToken);
            _dbl[name].LogInformation("Database created successfully");
            return false;
        }
        catch (Exception ex)
        {
            _dbl[name].LogError(ex, "Failed to create database");
            return true;
        }
    }
}