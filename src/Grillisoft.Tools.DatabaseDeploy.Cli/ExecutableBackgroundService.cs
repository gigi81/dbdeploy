using CommandLine.Text;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.Cli;

public class ExecutableBackgroundService : BackgroundService
{
    private readonly IExecutable _executable;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILogger<ExecutableBackgroundService> _logger;

    public ExecutableBackgroundService(
        IExecutable executable,
        IHostApplicationLifetime appLifetime,
        IHostEnvironment hostEnvironment,
        ILogger<ExecutableBackgroundService> logger)
    {
        _executable = executable;
        _appLifetime = appLifetime;
        _hostEnvironment = hostEnvironment;
        _logger = logger;
    }
    
    protected async override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            LogStartupInformation();
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

    private void LogStartupInformation()
    {
        _logger.LogInformation(HeadingInfo.Default.ToString());

        if(!string.IsNullOrWhiteSpace(_hostEnvironment.EnvironmentName))
            _logger.LogInformation("Environment {environment}", _hostEnvironment.EnvironmentName);
        else
            _logger.LogInformation("Environment was not specified. Using default");
    }
}