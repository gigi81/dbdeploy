namespace Grillisoft.Tools.DatabaseDeploy.Contracts;

public sealed class DatabaseConfig
{
    public string? Name { get; set; }
    public string? ConnectionString { get; set; }
    public string? Provider { get; set; }
}