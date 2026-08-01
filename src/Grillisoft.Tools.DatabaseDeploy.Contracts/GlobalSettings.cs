// ReSharper disable PropertyCanBeMadeInitOnly.Global
namespace Grillisoft.Tools.DatabaseDeploy.Contracts;

public class GlobalSettings
{
    public const string SectionName = "global";

    public string DefaultBranch { get; set; } = "main";

    public string DefaultProvider { get; set; } = string.Empty;

    public int ScriptTimeout { get; set; } = 60 * 60;

    public string StepsNameRegex { get; set; } = string.Empty;

    public string MigrationsTable { get; set; } = "__Migrations";

    public string InitStepName { get; set; } = "_Init";

    public bool RollbackRequired { get; set; } = true;

    /// <summary>
    /// Name of the script to run before a deploy starts. Empty means no script.
    /// </summary>
    public string PreDeploy { get; set; } = string.Empty;

    /// <summary>
    /// Name of the script to run after a deploy completed. Empty means no script.
    /// </summary>
    public string PostDeploy { get; set; } = string.Empty;

    /// <summary>
    /// Name of the script to run before a rollback starts. Empty means no script.
    /// </summary>
    public string PreRollback { get; set; } = string.Empty;

    /// <summary>
    /// Name of the script to run after a rollback completed. Empty means no script.
    /// </summary>
    public string PostRollback { get; set; } = string.Empty;
}