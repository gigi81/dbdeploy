using System.Threading;
using System.Threading.Tasks;
using Grillisoft.Tools.DatabaseDeploy.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.Services;

public class DeployService : BackgroundService
{
    private readonly DeployOptions _options;
    private readonly ILogger<DeployService> _logger;

    public DeployService(
        DeployOptions options,
        ILogger<DeployService> logger
    )
    {
        _options = options;
        _logger = logger;
    }
    
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.CompletedTask;
    }
}