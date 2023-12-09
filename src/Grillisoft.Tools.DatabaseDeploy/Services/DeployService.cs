using System;
using System.Collections;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Exceptions;
using Grillisoft.Tools.DatabaseDeploy.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.Services;

public class DeployService : BackgroundService
{
    private readonly DeployOptions _options;
    private readonly IFileSystem _fileSystem;
    private readonly IEnumerable<IDatabaseFactory> _databaseFactories;
    private readonly ILogger<DeployService> _logger;

    public DeployService(
        DeployOptions options,
        IFileSystem fileSystem,
        IEnumerable<IDatabaseFactory> databaseFactories,
        ILogger<DeployService> logger
    )
    {
        _options = options;
        _fileSystem = fileSystem;
        _databaseFactories = databaseFactories;
        _logger = logger;
    }
    
    protected async override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var manager = DeployManager.Load(_fileSystem.DirectoryInfo.New(_options.Path));
        var errors = manager.Validate();

        if (!manager.Branches.TryGetValue(_options.Branch, out var branch))
            throw new BranchNotFoundException(_options.Branch);

        var databases = branch.Databases.ToDictionary(d => d, GetDatabase);
        var migrations =
            branch.Databases.ToDictionary(d => d, d => new Queue<DatabaseMigration>(await databases[d].GetMigrations()));
        
        foreach (var step in branch.Steps)
        {
            if (migrations[step.Database].TryDequeue(out var migration))
            {
                if (!migration.Name.Equals(step.Name, StringComparison.InvariantCultureIgnoreCase))
                    throw new Exception("Sequence error detected"); //TODO: improve error
            }
            else
            {
                await databases[step.Database].Deploy(step);
            }
        }
    }

    private IDatabase GetDatabase(string name)
    {
        foreach (var factory in _databaseFactories)
        {
            var database = factory.GetDatabase(name);
            if (database != null)
                return database;
        }

        throw new Exception($"Database not found '{name}'");
    }
}