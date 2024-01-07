using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.Services;

public abstract class BaseService : BackgroundService
{
    protected readonly IEnumerable<IDatabaseFactory> _databaseFactories;
    protected readonly ILogger _logger;

    protected BaseService(IEnumerable<IDatabaseFactory> databaseFactories, ILogger logger)
    {
        _logger = logger;
        _databaseFactories = databaseFactories;
    }

    protected async Task RunScript(IFileInfo scriptFile, IDatabase database, CancellationToken cancellationToken)
    {
        _logger.LogInformation($"Parsing script {scriptFile.FullName}");
        var scripts = await database.ScriptParser.Parse(scriptFile, cancellationToken);
        
        _logger.LogInformation($"Running script {scriptFile.FullName}");
        await foreach (var script in scripts.WithCancellation(cancellationToken))
        {
            try
            {
                await database.RunScript(script, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to run script {0}", script);
                throw;
            }
        }
    }

    protected async Task<Dictionary<string, DatabaseInfo>> GetDatabases(IEnumerable<string> databases, CancellationToken cancellationToken)
    {
        var tasks = databases.Select(d => GetDatabase(d, cancellationToken)).ToArray();
        var databaseInfos = await Task.WhenAll(tasks);
        
        return databaseInfos.ToDictionary(d => d.Name, d => d);
    }
    
    protected async Task<DatabaseInfo> GetDatabase(string name, CancellationToken cancellationToken)
    {
        foreach (var factory in _databaseFactories)
        {
            var database = await factory.GetDatabase(name, cancellationToken);
            if (database != null)
            {
                var migrations = await database.GetMigrations(cancellationToken);
                return new(name, database, migrations.ToQueue());
            }
        }

        throw new Exception($"Database '{name}' not found");
    }
    
    protected record DatabaseInfo(string Name, IDatabase Database, Queue<DatabaseMigration> Migrations);
}