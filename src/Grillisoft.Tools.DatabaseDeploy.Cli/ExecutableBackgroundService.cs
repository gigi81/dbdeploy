using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.Cli;

public class ExecutableBackgroundService : BackgroundService
{
    private readonly IExecutable _executable;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly ILogger<ExecutableBackgroundService> _logger;

    public ExecutableBackgroundService(
        IExecutable executable,
        IHostApplicationLifetime appLifetime,
        ILogger<ExecutableBackgroundService> logger)
    {
        _executable = executable;
        _appLifetime = appLifetime;
        _logger = logger;
    }
    
    protected async override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _executable.Execute(stoppingToken);
            Environment.ExitCode = ExitCode.Ok;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, ex.Message);
            Environment.ExitCode = ExitCode.GenericError;
        }
        finally
        {
            // Stop the application once the work is done
            _appLifetime.StopApplication();
        }
    }
}