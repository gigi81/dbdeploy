﻿using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Grillisoft.Tools.DatabaseDeploy.PostgreSql;

internal class PostgreSqlDatabaseFactory : IDatabaseFactory
{
    public const string ProviderName = "postgreSql";

    private readonly PostgreSqlScriptParser _parser;
    private readonly IOptions<GlobalSettings> _globalSettings;
    private readonly ILoggerFactory _loggerFactory;

    public PostgreSqlDatabaseFactory(
        PostgreSqlScriptParser parser,
        IOptions<GlobalSettings> globalSettings,
        ILoggerFactory loggerFactory)
    {
        _parser = parser;
        _globalSettings = globalSettings;
        _loggerFactory = loggerFactory;
    }

    public string Name => ProviderName;

    public Task<IDatabase> GetDatabase(string name, IConfigurationSection config, CancellationToken cancellationToken)
    {
        var database = new PostgreSqlDatabase(
            name,
            config.GetValue("connectionString", string.Empty)!,
            config.GetValue("migrationTable", _globalSettings.Value.MigrationsTable)!,
            config.GetValue("scriptTimeout", _globalSettings.Value.ScriptTimeout),
            _parser,
            _loggerFactory.CreateLogger<PostgreSqlDatabase>());

        return Task.FromResult((IDatabase)database);
    }
}