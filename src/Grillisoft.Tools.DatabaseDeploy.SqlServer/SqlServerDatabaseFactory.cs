using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer;

internal class SqlServerDatabaseFactory : IDatabaseFactory
{
    private readonly SqlServerScriptParser _parser;

    public SqlServerDatabaseFactory(SqlServerScriptParser parser)
    {
        _parser = parser;
    }
    
    public string Name => "sqlServer";
    
    public Task<IDatabase> GetDatabase(string name, IConfigurationSection config, CancellationToken cancellationToken)
    {
        var database = new SqlServerDatabase(
            name,
            config["connectionString"] ?? "",
            config["migrationTable"] ?? "__Migration",
            _parser);

        return Task.FromResult((IDatabase)database);
    }
}