using System;
using System.IO.Abstractions;
using System.Linq;
using CommandLine;
using Grillisoft.Tools.DatabaseDeploy.Options;
using Grillisoft.Tools.DatabaseDeploy.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

try
{
    var result = Parser.Default.ParseArguments<DeployOptions, RollbackOptions>(args);

    if (result.Errors.Any())
    {
        Environment.ExitCode = ExitCode.GenericError;
        //TODO: print errors
    }
    else
    {
        await CreateHostBuilder(result.Value, args).RunConsoleAsync();
    }
}
catch(Exception ex)
{
    if(Environment.ExitCode == ExitCode.Ok)
        Environment.ExitCode = ExitCode.GenericError;

    Console.WriteLine(ex.Message);
    Console.WriteLine(ex);
}

static IHostBuilder CreateHostBuilder(object options, string[] args)
{
    return Host.CreateDefaultBuilder(args)
        //ctrl+C support
        .UseConsoleLifetime(options =>
        {
            options.SuppressStatusMessages = true;
        })
        .UseSerilog((hostingContext, services, loggerConfiguration) =>
        {
            loggerConfiguration.Enrich.FromLogContext()
                               .WriteTo.Console();
        })
        .ConfigureServices((hostContext, services) =>
        {
            services.AddSingleton<IFileSystem, FileSystem>()
                    .AddSingleton(options);

            switch (options)
            {
                case DeployOptions:
                    services.AddHostedService<DeployService>();
                    break;
                case RollbackOptions:
                    services.AddHostedService<RollbackService>();
                    break;
                default:
                    throw new Exception($"Options of type {options?.GetType().Name} not supported");
            }
        });
}

static class ExitCode
{
    public const int Ok = 0;
    public const int GenericError = -1;
}
