namespace Grillisoft.Tools.DatabaseDeploy.Contracts;
public record Branch
{
    public Branch(string name, IReadOnlyCollection<Step> steps)
    {
        this.Name = name;
        this.Steps = steps;
        this.Databases = steps.Select(s => s.Database).Distinct(StringComparer.InvariantCultureIgnoreCase).ToArray();
    }

    public string Name { get; }
    public IReadOnlyCollection<Step> Steps { get; }
    public IReadOnlyCollection<string> Databases { get; }
}