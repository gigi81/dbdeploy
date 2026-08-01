using System.Diagnostics;
using System.IO.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Soenneker.Extensions.String;

// ReSharper disable InconsistentNaming

namespace Grillisoft.Tools.DatabaseDeploy.Services;

public abstract class BaseService : IExecutable
{
    private readonly IDatabasesCollection _databases;
    private readonly IFileSystem _fileSystem;
    protected readonly IOptions<GlobalSettings> _globalSettings;
    protected readonly ILogger _logger;
    protected readonly DatabaseLoggerFactory _dbl;

    protected BaseService(
        IDatabasesCollection databases,
        IFileSystem fileSystem,
        IOptions<GlobalSettings> globalSettings,
        ILogger logger)
    {
        _databases = databases;
        _fileSystem = fileSystem;
        _globalSettings = globalSettings;
        _logger = logger;
        _dbl = new DatabaseLoggerFactory(logger);
    }

    public abstract Task<int> Execute(CancellationToken cancellationToken);

    protected IDirectoryInfo GetDirectory(string path)
    {
        return _fileSystem.DirectoryInfo.New(path);
    }

    protected async Task<BranchesReader> LoadBranches(string path, CancellationToken cancellationToken)
    {
        var directory = this.GetDirectory(path);
        var branches = new BranchesReader(directory, _globalSettings.Value, _databases.GetHooks);

        _logger.LogInformation("Loading branches from {Directory}", directory.FullName);
        var errors = await branches.Load();

        foreach (var error in errors)
            _logger.LogError(error);

        if (errors.Count > 0)
            throw new InvalidBranchesConfigurationException(errors);

        return branches;
    }

    protected async Task RunScripts(IEnumerable<IFileInfo> scriptFiles, IDatabase database, CancellationToken cancellationToken)
    {
        foreach (var scriptFile in scriptFiles)
        {
            await RunScript(scriptFile, database, cancellationToken);
        }
    }

    protected async Task RunScript(IFileInfo scriptFile, IDatabase database, CancellationToken cancellationToken)
    {
        _dbl[database.Name].LogInformation("Running script {ScriptPath}", scriptFile.FullName);
        var stopwatch = Stopwatch.StartNew();
        await foreach (var script in database.ScriptParser.Parse(scriptFile, cancellationToken))
        {
            try
            {
                await database.RunScript(script, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _dbl[database.Name].LogError(ex, "Failed to run script {ScriptContent}", script.Truncate(20_000));
                throw;
            }
        }
        _dbl[database.Name].LogInformation("Script {ScriptPath} executed in {ExecutionTime}", scriptFile.FullName, stopwatch.Elapsed);
    }

    /// <summary>
    /// Runs a hook script on every database, stopping at the first failure: this is what the
    /// scripts that run before a deploy or a rollback need, so that nothing starts after one of
    /// them failed.
    /// </summary>
    protected async Task RunHooks(
        DatabaseHook hook,
        IEnumerable<string> databases,
        IDirectoryInfo root,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        foreach (var database in databases)
        {
            await RunHook(hook, database, root, dryRun, cancellationToken);
        }
    }

    /// <summary>
    /// Runs a hook script on every database, carrying on after a failure: this is what the scripts
    /// that run after a deploy or a rollback need, as the work they follow is already done.
    /// </summary>
    /// <returns>The number of databases whose hook script failed</returns>
    protected async Task<int> TryRunHooks(
        DatabaseHook hook,
        IEnumerable<string> databases,
        IDirectoryInfo root,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var failed = 0;

        foreach (var database in databases)
        {
            try
            {
                await RunHook(hook, database, root, dryRun, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                //a cancellation is not a script failure and stops the run like anywhere else
                _dbl[database].LogError(ex, "Failed to run {Hook} script", hook);
                failed++;
            }
        }

        return failed;
    }

    private async Task RunHook(
        DatabaseHook hook,
        string database,
        IDirectoryInfo root,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var hooks = _databases.GetHooks(database);
        if (!hooks.IsConfigured(hook))
            return;

        var script = new HookScript(database, hook, hooks[hook], root);
        var file = script.File ?? throw new HookScriptNotFoundException(script);

        if (dryRun)
        {
            _dbl[database].LogInformation("Dry run: {Hook} script {ScriptPath} would be run", hook, file.FullName);
            return;
        }

        _dbl[database].LogInformation("Running {Hook} script", hook);
        var db = await GetDatabase(database, cancellationToken);
        await RunScript(file, db, cancellationToken);
    }

    protected async Task<Strategy> GetStrategy(Step[] steps, CancellationToken cancellationToken)
    {
        return new Strategy(steps, await GetAllMigrations(steps, cancellationToken), _logger);
    }

    private async Task<Dictionary<string, DatabaseMigration[]>> GetAllMigrations(Step[] steps, CancellationToken cancellationToken)
    {
        var tasks = steps.Select(s => s.Database)
            .Distinct()
            .Select(name => GetMigrations(name, cancellationToken))
            .ToArray();

        var tuples = await Task.WhenAll(tasks);

        return tuples.ToDictionary(
            m => m.Item1,
            m => m.Item2
        );
    }

    protected IReadOnlyCollection<string> Databases => _databases.Databases;

    protected async Task<IDatabase> GetDatabase(string name, CancellationToken cancellationToken)
    {
        return await _databases.GetDatabase(name, cancellationToken);
    }

    /// <summary>
    /// The formatter for a database's dialect, without building the database or needing a
    /// connection. Null when the configuration does not say which provider the database uses.
    /// </summary>
    protected ISqlFormatter? GetSqlFormatter(string name)
    {
        return _databases.GetSqlFormatter(name);
    }

    private async Task<(string, DatabaseMigration[])> GetMigrations(string name, CancellationToken cancellationToken)
    {
        var database = await _databases.GetDatabase(name, cancellationToken);
        var migrations = await database.GetMigrations(cancellationToken);
        _dbl[database.Name].LogInformation("Found {MigrationsCount} existing migrations in database", migrations.Count);
        return (name, migrations.ToArray());
    }
}