using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy;

/// <summary>
/// The names of the hook scripts of a database, already merged with the global settings.
/// An empty name means that the hook is not configured and nothing runs for it.
/// </summary>
public sealed class DatabaseHooks : IDatabaseHooks
{
    private readonly IDictionary<DatabaseHook, string> _hooks;
    private readonly string _databaseName;
    private readonly IDirectoryInfo _directory;

    public DatabaseHooks(IDictionary<DatabaseHook, string> hooks, string databaseName, IDirectoryInfo directory)
    {
        _hooks = hooks;
        _databaseName = databaseName;
        _directory = directory;
    }

    public IEnumerable<HookScript> HookScripts
    {
        get
        {
            foreach (var hook in Enum.GetValues<DatabaseHook>())
            {
                if (TryGetHookScript(hook, out var script))
                    yield return script;
            }
        }
    }

    public bool TryGetHookScript(DatabaseHook hook, [NotNullWhen(true)] out HookScript? script)
    {
        script = null;
        
        if (!_hooks.TryGetValue(hook, out var scriptName))
            return false;
        
        if(string.IsNullOrWhiteSpace(scriptName))
            return false;
        
        script = new HookScript(_databaseName, hook, scriptName, _directory);
        return true;
    }
}