namespace Grillisoft.Tools.DatabaseDeploy.Abstractions;

public interface IDatabaseFactory
{
    Task<IDatabase?> GetDatabase(string name, CancellationToken cancellationToken);
}
