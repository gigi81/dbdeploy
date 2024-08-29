using System.Diagnostics;
using System.IO.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Exceptions;
using Grillisoft.Tools.DatabaseDeploy.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Grillisoft.Tools.DatabaseDeploy.Services;

public class ValidateService : BaseService
{
    private readonly ValidateOptions _options;

    public ValidateService(
        ValidateOptions options,
        IDatabasesCollection databases,
        IFileSystem fileSystem,
        IOptions<GlobalSettings> globalSettings,
        ILogger logger)
        : base(databases, fileSystem, globalSettings, logger)
    {
        _options = options;
    }

    public async override Task<int> Execute(CancellationToken cancellationToken)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();
            _logger.LogInformation("Starting validation");
            await LoadBranchesManager(_options.Path, cancellationToken);
            _logger.LogInformation("Validation completed successfully in {Elapsed}", stopwatch.Elapsed);
            return 0;
        }
        catch (InvalidBranchesConfigurationException ex)
        {
            _logger.LogError(ex, "Validation failed with {ErrorsCount} errors", ex.Errors.Count);
            return ex.Errors.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Validation failed with error {ErrorMessage} errors", ex.Message);
            return -1;
        }
    }
}