using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.Contracts;

/// <summary>
/// The names of the hook scripts of a database, already merged with the global settings.
/// An empty name means that the hook is not configured and nothing runs for it.
/// </summary>
public sealed record DatabaseHooks(IDictionary<DatabaseHook, string> Hooks)
{
    public static readonly DatabaseHooks None =
        new(Enum.GetValues<DatabaseHook>().ToDictionary(hook => hook, _ => string.Empty));

    public IEnumerable<HookScript> GetHookScripts(string database, IDirectoryInfo directory)
    {
        foreach (var hook in Enum.GetValues<DatabaseHook>())
        {
            if (TryGetHookScript(hook, database, directory, out var script))
                yield return script;
        }
    }

    public bool TryGetHookScript(DatabaseHook hook, string database, IDirectoryInfo directory, [NotNullWhen(true)] out HookScript? script)
    {
        script = null;
        
        if (!this.Hooks.TryGetValue(hook, out var scriptName))
            return false;
        
        if(string.IsNullOrWhiteSpace(scriptName))
            return false;
        
        script = new HookScript(database, hook, scriptName, directory);
        return true;
    }
}
