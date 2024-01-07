using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Threading;
using System.Threading.Tasks;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Exceptions;
using Grillisoft.Tools.DatabaseDeploy.Options;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.Services;

public class DeployService : BaseService
{
    private readonly DeployOptions _options;
    private readonly IFileSystem _fileSystem;

    public DeployService(
        DeployOptions options,
        IFileSystem fileSystem,
        IEnumerable<IDatabaseFactory> databaseFactories,
        ILogger<DeployService> logger
    ) : base(databaseFactories, logger)
    {
        _options = options;
        _fileSystem = fileSystem;
    }
    
    protected async override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var manager = DeployManager.Load(_fileSystem.DirectoryInfo.New(_options.Path));
        var errors = manager.Validate();

        if (!manager.Branches.TryGetValue(_options.Branch, out var branch))
            throw new BranchNotFoundException(_options.Branch);

        var databases = await GetDatabases(branch.Databases, stoppingToken);
        
        foreach (var step in branch.Steps)
        {
            var (_, database, migrations) = databases[step.Database];
            
            if (migrations.TryDequeue(out var migration))
            {
                if (!migration.Name.Equals(step.Name, StringComparison.InvariantCultureIgnoreCase))
                    throw new StepMigrationMissmatchException(step, migration);
                
                _logger.LogInformation($"Step {step.Name} already deployed");
            }
            else
            {
                await RunScript(step.DeployScript, database, stoppingToken);
                if(_options.UnitTest)
                    await RunScript(step.TestScript, database, stoppingToken);

                _logger.LogInformation($"Adding migration {step.Name}");
                var migrationToAdd = new DatabaseMigration(
                    step.Name,
                    DateTimeOffset.UtcNow,
                    Environment.UserName,
                    step.DeployScript.Name);
                
                await database.AddMigration(migrationToAdd, stoppingToken);
            }
        }
    }
}