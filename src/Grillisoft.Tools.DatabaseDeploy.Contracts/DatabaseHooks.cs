using System.IO.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.Contracts;

/// <summary>
/// The names of the hook scripts of a database, already merged with the global settings.
/// An empty name means that the hook is not configured and nothing runs for it.
/// </summary>
public sealed record DatabaseHooks(string PreDeploy, string PostDeploy, string PreRollback, string PostRollback)
{
    public static readonly DatabaseHooks None =
        new(string.Empty, string.Empty, string.Empty, string.Empty);

    public string this[DatabaseHook hook] => hook switch
    {
        DatabaseHook.PreDeploy => this.PreDeploy,
        DatabaseHook.PostDeploy => this.PostDeploy,
        DatabaseHook.PreRollback => this.PreRollback,
        DatabaseHook.PostRollback => this.PostRollback,
        _ => throw new ArgumentOutOfRangeException(nameof(hook), hook, "Unknown database hook")
    };

    public bool IsConfigured(DatabaseHook hook) => !string.IsNullOrWhiteSpace(this[hook]);

    /// <summary>
    /// The hooks that have a script name configured.
    /// </summary>
    public IEnumerable<DatabaseHook> Configured =>
        Enum.GetValues<DatabaseHook>().Where(IsConfigured);
    
    public IEnumerable<HookScript> GetHookScripts(string database, IDirectoryInfo directory)
    {
        return this.Configured.Select(hook => new HookScript(database, hook, this[hook], directory));
    }
}
