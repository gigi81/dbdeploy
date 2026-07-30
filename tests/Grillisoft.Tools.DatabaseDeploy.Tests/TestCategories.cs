namespace Grillisoft.Tools.DatabaseDeploy.Tests;

/// <summary>
/// The categories CI filters on.
/// </summary>
public static class TestCategories
{
    /// <summary>
    /// A test that needs a Docker engine, because it runs against a Testcontainers database. The
    /// Windows and macOS legs of the build have no engine, so they run everything else with
    /// <c>--treenode-filter "/*/*/*/*[Category!=Docker]"</c>.
    /// </summary>
    public const string Docker = "Docker";
}
