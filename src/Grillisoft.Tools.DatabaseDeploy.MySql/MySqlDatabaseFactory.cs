using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.MySql;

public class MySqlDatabaseFactory : IDatabaseFactory
{
    private readonly MySqlScriptParser _parser;
    private readonly ILoggerFactory _loggerFactory;

    public MySqlDatabaseFactory(MySqlScriptParser parser, ILoggerFactory loggerFactory)
    {
        _parser = parser;
        _loggerFactory = loggerFactory;
    }
    
    public string Name => "mySql";
    
    public Task<IDatabase> GetDatabase(string name, IConfigurationSection config, CancellationToken cancellationToken)
    { 
        var database = new MySqlDatabase(
            name,
            config["connectionString"] ?? "",
            config["migrationTable"] ?? "__Migration",
            _parser,
            _loggerFactory.CreateLogger<MySqlDatabase>());

        return Task.FromResult((IDatabase)database);
    }
}