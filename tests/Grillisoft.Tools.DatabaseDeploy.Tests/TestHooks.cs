using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy.Tests;

/// <summary>
/// Builds the <see cref="DatabaseHooks"/> of a database, where naming the one or two hooks a test
/// is about reads better than filling a dictionary with every one of them. A hook that is left out
/// is one that is not configured, same as one with an empty name.
/// </summary>
public static class TestHooks
{
    public static DatabaseHooks Of(params (DatabaseHook Hook, string Script)[] hooks) =>
        new(hooks.ToDictionary(hook => hook.Hook, hook => hook.Script));

    /// <summary>The same hooks with one of them renamed, or turned off with an empty name.</summary>
    public static DatabaseHooks With(this DatabaseHooks hooks, DatabaseHook hook, string script) =>
        new(new Dictionary<DatabaseHook, string>(hooks.Hooks) { [hook] = script });
}
