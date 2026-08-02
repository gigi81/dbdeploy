using System.Diagnostics;
using Grillisoft.Tools.DatabaseDeploy.Exceptions;
using Grillisoft.Tools.DatabaseDeploy.Options;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.Services;

public class ValidateService : BaseService
{
    public ValidateService(
        ValidateOptions options,
        ServiceDependencies dependencies,
        ILogger<ValidateService> logger)
        : base(dependencies, logger)
    {
    }

    public async override Task<int> Execute(CancellationToken cancellationToken)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();
            _logger.LogInformation("Starting validation");
            await LoadBranches(cancellationToken);
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