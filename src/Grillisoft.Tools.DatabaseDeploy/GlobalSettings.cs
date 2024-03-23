namespace Grillisoft.Tools.DatabaseDeploy;

public class GlobalSettings
{
    public const string SectionName = "global";
    
    public string DefaultProvider { get; set; } = string.Empty;

    public int ScriptsTimeout { get; set; } = 60 * 60;

    public string ScriptsNameRegex { get; set; } = string.Empty;

    public string DefaultMigrationTableName { get; set; } = "__Migrations";

    public string DefaultInitScriptName { get; set; } = "_Init";

    public bool RollbackScriptsRequired { get; set; } = true;
}