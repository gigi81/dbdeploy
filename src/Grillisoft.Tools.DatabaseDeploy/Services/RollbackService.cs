using System.Threading;
using System.Threading.Tasks;
using Grillisoft.Tools.DatabaseDeploy.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.Services;

public class RollbackService : BackgroundService
{
    private readonly RollbackOptions _options;
    private readonly ILogger<RollbackService> _logger;

    public RollbackService(
        RollbackOptions options,
        ILogger<RollbackService> logger)
    {
        _options = options;
        _logger = logger;
    }
    
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => throw new System.NotImplementedException();
}