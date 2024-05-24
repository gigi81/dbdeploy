using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle;

public class OracleDatabaseFactory : IDatabaseFactory
{
    private readonly OracleScriptParser _parser;
    private readonly IOptions<GlobalSettings> _globalSettings;
    private readonly ILoggerFactory _loggerFactory;

    public OracleDatabaseFactory(
        OracleScriptParser parser,
        IOptions<GlobalSettings> globalSettings,
        ILoggerFactory loggerFactory)
    {
        _parser = parser;
        _globalSettings = globalSettings;
        _loggerFactory = loggerFactory;
    }
    
    public string Name => "oracle";
    
    public Task<IDatabase> GetDatabase(string name, IConfigurationSection config, CancellationToken cancellationToken)
    { 
        var database = new OracleDatabase(
            name,
            config.GetValue("schema", string.Empty)!,
            config.GetValue("connectionString", string.Empty)!,
            config.GetValue("migrationTable", _globalSettings.Value.MigrationsTable)!,
            config.GetValue("scriptTimeout", _globalSettings.Value.ScriptTimeout),
            _parser,
            _loggerFactory.CreateLogger<OracleDatabase>());

        return Task.FromResult((IDatabase)database);
    }
}