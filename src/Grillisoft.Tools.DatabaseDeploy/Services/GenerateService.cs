using System.Diagnostics;
using System.IO.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Grillisoft.Tools.DatabaseDeploy.Services;

public class GenerateService : BaseService
{
    private readonly IGenerator _generator;
    private readonly IDirectoryInfo _directory;

    public GenerateService(
        GenerateOptions options,
        IGenerator generator,
        IDatabasesCollection databases,
        IFileSystem fileSystem,
        IOptions<GlobalSettings> globalSettings,
        ILogger<GenerateService> logger)
        : base(databases, fileSystem, globalSettings, logger)
    {
        _generator = generator;
        _directory = fileSystem.DirectoryInfo.New(options.Path);
    }

    public async override Task<int> Execute(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Searching for missing rollback scripts on path {Path}", _directory.FullName);

        var errors = 0;
        var stopwatch = Stopwatch.StartNew();
        var missingRollbacks = _directory.GetFiles("*.Deploy.sql", SearchOption.AllDirectories)
            .Where(file => !GetRollbackFile(file).Exists)
            .ToArray();

        if (missingRollbacks.Length <= 0)
        {
            _logger.LogWarning("No missing rollback scripts found on path {Path}", _directory.FullName);
            return 0;
        }
        
        foreach (var deployFile in missingRollbacks)
        {
            var rollbackFile = GetRollbackFile(deployFile);
            _logger.LogInformation("Generating rollback script {Path}", rollbackFile.FullName);

            try
            {
                var database = await GetDatabase(deployFile.Directory?.Name ?? "", cancellationToken);
                await _generator.GenerateRollback(deployFile, rollbackFile, database.Dialect, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate rollback script {Path}", rollbackFile.FullName);
                errors++;
            }
        }
        
        _logger.LogInformation("Generated {Count} rollback scripts in {Elapsed}", missingRollbacks.Length, stopwatch.Elapsed);
        return errors;
    }
    
    private static IFileInfo GetRollbackFile(IFileInfo deployFile)
    {
        return deployFile.Directory.File(deployFile.Name.Replace(".Deploy.sql", ".Rollback.sql"));
    }
}