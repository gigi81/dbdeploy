namespace Grillisoft.Tools.DatabaseDeploy.Abstractions;

public interface IDatabasesCollection
{
    IReadOnlyCollection<string> Databases { get; }
    Task<IDatabase> GetDatabase(string name, CancellationToken cancellationToken);
}