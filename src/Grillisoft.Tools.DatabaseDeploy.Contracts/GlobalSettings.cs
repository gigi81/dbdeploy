namespace Grillisoft.Tools.DatabaseDeploy.Contracts;

public class GlobalSettings
{
    public const string SectionName = "global";
    
    public string DefaultProvider { get; set; } = string.Empty;

    public int ScriptTimeout { get; set; } = 60 * 60;

    public string StepsNameRegex { get; set; } = string.Empty;

    public string MigrationsTable { get; set; } = "__Migrations";

    public string InitStepName { get; set; } = "_Init";

    public bool RollbackRequired { get; set; } = true;
}