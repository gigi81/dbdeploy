using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Grillisoft.Tools.DatabaseDeploy.MySql;

public class MySqlDatabaseFactory : IDatabaseFactory
{
    private readonly MySqlScriptParser _parser;

    public MySqlDatabaseFactory(MySqlScriptParser parser)
    {
        _parser = parser;
    }
    
    public string Name => "mySql";
    
    public Task<IDatabase> GetDatabase(string name, IConfigurationSection config, CancellationToken cancellationToken)
    { 
        var database = new MySqlDatabase(
            name,
            config["connectionString"] ?? "",
            config["migrationTable"] ?? "__Migration",
            _parser);

        return Task.FromResult((IDatabase)database);
    }
}