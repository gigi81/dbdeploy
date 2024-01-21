using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer;

internal class SqlServerDatabaseFactory : IDatabaseFactory
{
    public string Name => "sqlServer";
    
    public Task<IDatabase> GetDatabase(DatabaseConfig config, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}