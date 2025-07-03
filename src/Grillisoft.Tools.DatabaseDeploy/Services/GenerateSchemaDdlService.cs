using System.IO.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Grillisoft.Tools.DatabaseDeploy.Services;

public class GenerateSchemaDdlService : BaseService
{
    private readonly GenerateSchemaDdlOptions _options;
    private readonly IProgress<int> _progress;

    public GenerateSchemaDdlService(
        GenerateSchemaDdlOptions options,
        IDatabasesCollection databases,
        IFileSystem fileSystem,
        IOptions<GlobalSettings> globalOptions,
        IProgress<int> progress,
        ILogger<GenerateSchemaDdlService> logger
    ) : base(databases, fileSystem, globalOptions, logger)
    {
        _options = options;
        _progress = progress;
    }
    
    public async override Task<int> Execute(CancellationToken cancellationToken)
    {
        foreach (var databaseName in this.Databases)
        {
            var database = await this.GetDatabase(databaseName, cancellationToken);
            var databaseDirectory = this.GetDirectory(_options.Path).SubDirectory(databaseName);
            var initFile = databaseDirectory.File("Init.sql");
            
            databaseDirectory.Create();
            _logger.LogInformation("Generating schema for {DatabaseName} to {Filename}", databaseName, initFile.FullName);
            await using var stream = initFile.OpenWrite();
            await database.GenerateSchemaDdl(stream, cancellationToken);
        }

        return 0;
    }
}