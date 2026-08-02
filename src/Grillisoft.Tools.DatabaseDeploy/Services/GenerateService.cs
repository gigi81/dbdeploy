using System.Diagnostics;
using System.IO.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Options;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.Services;

public class GenerateService : BaseService
{
    private readonly IGenerator _generator;

    public GenerateService(
        GenerateOptions _,
        IGenerator generator,
        ServiceDependencies dependencies,
        ILogger<GenerateService> logger)
        : base(dependencies, logger)
    {
        _generator = generator;
    }

    public async override Task<int> Execute(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Searching for missing rollback scripts on path {Path}", _rootDirectory.FullName);

        var errors = 0;
        var stopwatch = Stopwatch.StartNew();
        var missingRollbacks = _rootDirectory.GetFiles("*.Deploy.sql", SearchOption.AllDirectories)
            .Where(file => !GetRollbackFile(file).Exists)
            .ToArray();

        if (missingRollbacks.Length <= 0)
        {
            _logger.LogWarning("No missing rollback scripts found on path {Path}", _rootDirectory.FullName);
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