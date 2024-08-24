using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Options;
using Grillisoft.Tools.DatabaseDeploy.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Grillisoft.Tools.DatabaseDeploy.Cli;

public static class Extensions
{
    internal static IServiceCollection AddExecutable(this IServiceCollection services, OptionsBase options)
    {
        switch (options)
        {
            case DeployOptions deployOptions:
                services.AddSingleton(deployOptions);
                services.AddSingleton<IExecutable, DeployService>();
                break;

            case RollbackOptions rollbackOptions:
                services.AddSingleton(rollbackOptions);
                services.AddSingleton<IExecutable, RollbackService>();
                break;
            
            case CiOptions ciOptions:
                services.AddSingleton(ciOptions);
                services.AddSingleton<IExecutable, CiService>();
                break;

            default:
                throw new ArgumentException($"Options of type {options.GetType().Name} not supported", nameof(options));
        }

        return services;
    }
}