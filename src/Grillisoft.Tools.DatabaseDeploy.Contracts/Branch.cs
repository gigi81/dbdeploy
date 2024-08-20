namespace Grillisoft.Tools.DatabaseDeploy.Contracts;
public record Branch
{
    private readonly Lazy<string[]> _databases;

    public Branch(string name, IReadOnlyCollection<Step> steps)
    {
        this.Name = name;
        this.Steps = steps;
        _databases = new Lazy<string[]>(() =>
            steps.Select(s => s.Database).DistinctIgnoreCase().ToArray());
    }

    public string Name { get; }
    public IReadOnlyCollection<Step> Steps { get; }

    public IReadOnlyCollection<string> Databases => _databases.Value;
}