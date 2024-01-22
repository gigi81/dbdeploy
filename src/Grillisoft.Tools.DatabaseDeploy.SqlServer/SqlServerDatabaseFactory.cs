using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer;

internal class SqlServerDatabaseFactory : IDatabaseFactory
{
    private readonly SqlServerScriptParser _parser;
    private readonly ILoggerFactory _loggerFactory;

    public SqlServerDatabaseFactory(SqlServerScriptParser parser, ILoggerFactory loggerFactory)
    {
        _parser = parser;
        _loggerFactory = loggerFactory;
    }
    
    public string Name => "sqlServer";
    
    public Task<IDatabase> GetDatabase(string name, IConfigurationSection config, CancellationToken cancellationToken)
    {
        var database = new SqlServerDatabase(
            name,
            config["connectionString"] ?? "",
            config["migrationTable"] ?? "__Migration",
            _parser,
            _loggerFactory.CreateLogger<SqlServerDatabase>());

        return Task.FromResult((IDatabase)database);
    }
}