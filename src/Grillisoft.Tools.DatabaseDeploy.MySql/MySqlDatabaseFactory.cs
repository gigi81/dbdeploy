using Grillisoft.Tools.DatabaseDeploy.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy;

public class MySqlDatabaseFactory : IDatabaseFactory
{
    public Task<IDatabase?> GetDatabase(string name, CancellationToken cancellationToken) => throw new NotImplementedException();
}