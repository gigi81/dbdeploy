using System.IO.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.Abstractions;

/// <summary>
/// Runs a script file against a database: parses it into the batches of its dialect, runs them in
/// order and logs what ran and for how long. It knows nothing about steps, branches or hooks.
/// </summary>
public interface IScriptsRunner
{
    Task Run(IEnumerable<IFileInfo> scriptFiles, IDatabase database, CancellationToken cancellationToken);

    Task Run(IFileInfo scriptFile, IDatabase database, CancellationToken cancellationToken);
}
