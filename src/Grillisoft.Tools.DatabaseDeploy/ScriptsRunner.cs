using System.Diagnostics;
using System.IO.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Microsoft.Extensions.Logging;
using Soenneker.Extensions.String;

namespace Grillisoft.Tools.DatabaseDeploy;

/// <inheritdoc cref="IScriptsRunner"/>
public class ScriptsRunner : IScriptsRunner
{
    private readonly IDatabaseLoggerFactory _dbl;

    public ScriptsRunner(IDatabaseLoggerFactory databaseLoggers)
    {
        _dbl = databaseLoggers;
    }

    public async Task Run(IEnumerable<IFileInfo> scriptFiles, IDatabase database, CancellationToken cancellationToken)
    {
        foreach (var scriptFile in scriptFiles)
        {
            await Run(scriptFile, database, cancellationToken);
        }
    }

    public async Task Run(IFileInfo scriptFile, IDatabase database, CancellationToken cancellationToken)
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
}
