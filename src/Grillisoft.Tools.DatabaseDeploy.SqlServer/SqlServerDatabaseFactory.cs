using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer;

internal class SqlServerDatabaseFactory : IDatabaseFactory
{
    private readonly SqlServerScriptParser _parser;
    private readonly GlobalSettings _globalSettings;
    private readonly ILoggerFactory _loggerFactory;

    public SqlServerDatabaseFactory(
        SqlServerScriptParser parser,
        GlobalSettings globalSettings,
        ILoggerFactory loggerFactory)
    {
        _parser = parser;
        _globalSettings = globalSettings;
        _loggerFactory = loggerFactory;
    }
    
    public string Name => "sqlServer";
    
    public Task<IDatabase> GetDatabase(string name, IConfigurationSection config, CancellationToken cancellationToken)
    {
        var database = new SqlServerDatabase(
            name,
            config.GetValue("connectionString", string.Empty)!,
            config.GetValue("migrationTable", _globalSettings.MigrationsTable)!,
            config.GetValue("scriptTimeout", _globalSettings.ScriptTimeout),
            _parser,
            _loggerFactory.CreateLogger<SqlServerDatabase>());

        return Task.FromResult((IDatabase)database);
    }
}