namespace Grillisoft.Tools.DatabaseDeploy.Abstractions;

public interface IDatabasesCollection
{
    Task<IDatabase> GetDatabase(string name, CancellationToken cancellationToken);
}