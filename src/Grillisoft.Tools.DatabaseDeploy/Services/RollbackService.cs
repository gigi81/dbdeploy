using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Exceptions;
using Grillisoft.Tools.DatabaseDeploy.Options;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.Services;

public class RollbackService : BaseService
{
    private readonly RollbackOptions _options;
    private readonly IFileSystem _fileSystem;

    public RollbackService(
        RollbackOptions options,
        IFileSystem fileSystem,
        IEnumerable<IDatabaseFactory> databaseFactories,
        ILogger<RollbackService> logger
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

        var databases = await GetDatabases(branch.Databases);
        
        foreach (var step in branch.Steps.Reverse())
        {
            var (_, database, migrations) = databases[step.Database];
            
            //TODO
        }
    }
}