using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy.Tests;

/// <summary>
/// The hook script names a <see cref="DatabaseHooks"/> is built from, where naming the one or two
/// hooks a test is about reads better than filling a dictionary with every one of them. A hook that
/// is left out is one that is not configured, same as one with an empty name. The folder they are
/// rooted at belongs to the test's own file system, so it is given when the hooks are built.
/// </summary>
public static class TestHooks
{
    public static IDictionary<DatabaseHook, string> None => Of();

    public static IDictionary<DatabaseHook, string> Of(params (DatabaseHook Hook, string Script)[] hooks) =>
        hooks.ToDictionary(hook => hook.Hook, hook => hook.Script);

    /// <summary>The same hooks with one of them renamed, or turned off with an empty name.</summary>
    public static IDictionary<DatabaseHook, string> With(
        this IDictionary<DatabaseHook, string> hooks,
        DatabaseHook hook,
        string script) =>
        new Dictionary<DatabaseHook, string>(hooks) { [hook] = script };
}
