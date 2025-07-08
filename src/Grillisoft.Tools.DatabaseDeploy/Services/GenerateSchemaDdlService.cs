using System.Diagnostics;
using System.IO.Abstractions;
using System.Text;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Grillisoft.Tools.DatabaseDeploy.Services;

public class GenerateSchemaDdlService : BaseService
{
    private readonly GenerateSchemaDdlOptions _options;

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
    }

    public async override Task<int> Execute(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting generating database(s) schema definitions for {Count} databases", this.Databases.Count);
        var stopwatch = Stopwatch.StartNew();
        var rootDirectory = this.GetDirectory(_options.Path);
        var lines = new List<string>();

        foreach (var databaseName in this.Databases)
        {
            if (await GenerateDatabaseDdl(databaseName, rootDirectory, cancellationToken))
                lines.Add($"{databaseName},{_globalSettings.Value.InitStepName}");
        }

        rootDirectory
            .File(_globalSettings.Value.DefaultBranch + ".csv")
            .AppendAllLines(lines);

        _logger.LogInformation("Schema definitions completed in {ElapsedTime}", stopwatch.Elapsed);
        return 0;
    }

    private async Task<bool> GenerateDatabaseDdl(string databaseName, IDirectoryInfo rootDirectory, CancellationToken cancellationToken)
    {
        var databaseDirectory = rootDirectory.SubDirectory(databaseName);
        var initFile = databaseDirectory.File(_globalSettings.Value.InitStepName + ".sql");
        if (initFile.Exists)
        {
            _logger.LogWarning("Init file already exists for schema {SchemaName}", databaseName);
            return false;
        }

        var database = await this.GetDatabase(databaseName, cancellationToken);

        databaseDirectory.Create();
        _logger.LogInformation("Generating schema for {DatabaseName} to {Filename}", databaseName, initFile.FullName);
        await using var stream = initFile.OpenWrite();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true);
        await database.GenerateSchemaDdl(writer, cancellationToken);
        return true;
    }
}