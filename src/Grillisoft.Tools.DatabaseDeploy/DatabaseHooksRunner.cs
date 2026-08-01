using System.IO.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Exceptions;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy;

/// <summary>
/// Runs the scripts configured around a deploy or a rollback. Everything a hook needs is here:
/// which script a database has configured, where its file is and what a failure means.
/// </summary>
public class DatabaseHooksRunner
{
    private readonly IDatabasesCollection _databases;
    private readonly IDirectoryInfo _root;
    private readonly bool _dryRun;
    private readonly ScriptsRunner _scripts;
    private readonly DatabaseLoggerFactory _dbl;

    /// <param name="root">The folder the scripts are looked up in, the one of the branch files</param>
    /// <param name="dryRun">When set the scripts are only reported, like every other script</param>
    public DatabaseHooksRunner(IDatabasesCollection databases, IDirectoryInfo root, bool dryRun, ILogger logger)
    {
        _databases = databases;
        _root = root;
        _dryRun = dryRun;
        _scripts = new ScriptsRunner(logger);
        _dbl = new DatabaseLoggerFactory(logger);
    }

    /// <summary>
    /// Runs a hook script on every database, stopping at the first failure: this is what the
    /// scripts that run before a deploy or a rollback need, so that nothing starts after one of
    /// them failed.
    /// </summary>
    public async Task Run(DatabaseHook hook, IEnumerable<string> databases, CancellationToken cancellationToken)
    {
        foreach (var database in databases)
        {
            await RunHook(hook, database, cancellationToken);
        }
    }

    /// <summary>
    /// Runs a hook script on every database, carrying on after a failure: this is what the scripts
    /// that run after a deploy or a rollback need, as the work they follow is already done.
    /// </summary>
    /// <returns>The number of databases whose hook script failed</returns>
    public async Task<int> TryRun(DatabaseHook hook, IEnumerable<string> databases, CancellationToken cancellationToken)
    {
        var failed = 0;

        foreach (var database in databases)
        {
            try
            {
                await RunHook(hook, database, cancellationToken);
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

    private async Task RunHook(DatabaseHook hook, string database, CancellationToken cancellationToken)
    {
        var hooks = _databases.GetHooks(database);
        if (!hooks.IsConfigured(hook))
            return;

        var script = new HookScript(database, hook, hooks[hook], _root);
        var file = script.File ?? throw new HookScriptNotFoundException(script);

        if (_dryRun)
        {
            _dbl[database].LogInformation("Dry run: {Hook} script {ScriptPath} would be run", hook, file.FullName);
            return;
        }

        _dbl[database].LogInformation("Running {Hook} script", hook);
        var db = await _databases.GetDatabase(database, cancellationToken);
        await _scripts.Run(file, db, cancellationToken);
    }
}
