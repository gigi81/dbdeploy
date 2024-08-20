namespace Grillisoft.Tools.DatabaseDeploy.Contracts;
public record Branch
{
    private string[]? _databases;

    public Branch(string name, IReadOnlyCollection<Step> steps)
    {
        this.Name = name;
        this.Steps = steps;
    }

    public string Name { get; }
    public IReadOnlyCollection<Step> Steps { get; }

    public IReadOnlyCollection<string> Databases =>
        _databases ??= this.Steps.Select(s => s.Database).Distinct(StringComparer.InvariantCultureIgnoreCase).ToArray();
}