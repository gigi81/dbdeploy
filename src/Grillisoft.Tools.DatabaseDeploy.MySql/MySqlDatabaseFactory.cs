using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy.MySql;

public class MySqlDatabaseFactory : IDatabaseFactory
{
    public string Name => "mySql";
    
    public Task<IDatabase> GetDatabase(DatabaseConfig config, CancellationToken cancellationToken) => throw new NotImplementedException();
}