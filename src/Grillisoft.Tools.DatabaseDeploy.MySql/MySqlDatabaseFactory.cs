using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Microsoft.Extensions.Configuration;

namespace Grillisoft.Tools.DatabaseDeploy.MySql;

public class MySqlDatabaseFactory : IDatabaseFactory
{
    public string Name => "mySql";
    
    public Task<IDatabase> GetDatabase(IConfigurationSection config, CancellationToken cancellationToken)
        => throw new NotImplementedException();
}