using System.Diagnostics.CodeAnalysis;
using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy.Abstractions;

/// <summary>
/// The hook scripts a database has configured, already merged with the global settings and rooted
/// at the folder they are looked up in. A hook that is not configured simply has no script.
/// </summary>
public interface IDatabaseHooks
{
    /// <summary>
    /// The scripts of every hook that is configured, in the order the hooks are declared in.
    /// </summary>
    IEnumerable<HookScript> HookScripts { get; }

    /// <summary>
    /// The script of one hook, when it is configured.
    /// </summary>
    /// <param name="hook">Which of the hooks to look up</param>
    /// <param name="script">The script to run, when there is one</param>
    /// <returns>False when the hook is not configured, which is not an error</returns>
    bool TryGetHookScript(DatabaseHook hook, [NotNullWhen(true)] out HookScript? script);
}
