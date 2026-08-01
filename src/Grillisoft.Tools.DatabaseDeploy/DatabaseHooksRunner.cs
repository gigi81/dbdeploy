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
    private readonly IScriptsRunner _scripts;
    private readonly IDatabaseLoggerFactory _dbl;

    /// <param name="databases">Where the hooks of a database and the database itself come from</param>
    /// <param name="root">The folder the scripts are looked up in, the one of the branch files</param>
    /// <param name="dryRun">When set the scripts are only reported, like every other script</param>
    /// <param name="scripts">Runs a hook script the same way a step script is run</param>
    /// <param name="databaseLoggers">Where the logger of a database comes from</param>
    public DatabaseHooksRunner(
        IDatabasesCollection databases,
        IDirectoryInfo root,
        bool dryRun,
        IScriptsRunner scripts,
        IDatabaseLoggerFactory databaseLoggers)
    {
        _databases = databases;
        _root = root;
        _dryRun = dryRun;
        _scripts = scripts;
        _dbl = databaseLoggers;
    }

    /// <summary>
    /// Runs a hook script on every database, stopping at the first failure: this is what the
    /// scripts that run before a deploy or a rollback need, so that nothing starts after one of
    /// them failed.
    /// </summary>
    /// <param name="hook">Which of the four scripts to run</param>
    /// <param name="databases">The databases to run it for, the ones with steps in the plan</param>
    /// <param name="cancellationToken">Cancels the run, and is not treated as a script failure</param>
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
    /// <param name="hook">Which of the four scripts to run</param>
    /// <param name="databases">The databases to run it for, the ones with steps in the plan</param>
    /// <param name="cancellationToken">Cancels the run, and is not treated as a script failure</param>
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
