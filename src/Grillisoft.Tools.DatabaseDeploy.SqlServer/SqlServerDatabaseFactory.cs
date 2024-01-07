using Grillisoft.Tools.DatabaseDeploy.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer;

internal class SqlServerDatabaseFactory : IDatabaseFactory
{
    public Task<IDatabase?> GetDatabase(string name, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}