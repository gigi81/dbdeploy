using System.IO.Abstractions;
using CommandLine;
using Grillisoft.Tools.DatabaseDeploy;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Cli;
using Grillisoft.Tools.DatabaseDeploy.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

try
{
    var result = Parser.Default.ParseArguments<DeployOptions, RollbackOptions>(args);

    if (result.Errors.Any())
    {
        Environment.ExitCode = ExitCode.InvalidArguments;
        //TODO: print errors
    }
    else
    {
        await CreateHostBuilder((OptionsBase)result.Value, args).RunConsoleAsync();
    }
}
catch(Exception ex)
{
    if(Environment.ExitCode == ExitCode.Ok)
        Environment.ExitCode = ExitCode.GenericError;

    Console.WriteLine(ex.Message);
}

static IHostBuilder CreateHostBuilder(OptionsBase options, string[] args)
{
    return Host.CreateDefaultBuilder(args)
        //ctrl+C support
        .UseConsoleLifetime(options =>
        {
            options.SuppressStatusMessages = true;
        })
        .UseSerilog((hostingContext, services, loggerConfiguration) =>
        {
            loggerConfiguration
                .Enrich.FromLogContext()
                .WriteTo.Console();
        })
        .ConfigureAppConfiguration((builder, config) =>
        {
            var environmentName = builder.HostingEnvironment.EnvironmentName;
            var configRoot = Path.GetFullPath(options.Path);
            config.AddJsonFile(Path.Combine(configRoot, "dbsettings.json"), optional: false);
            config.AddJsonFile(Path.Combine(configRoot, $"dbsettings.{environmentName}.json"), optional: true);
        })
        .ConfigureServices((builder, services) =>
        {
            services.Configure<GlobalSettings>(
                builder.Configuration.GetSection(GlobalSettings.SectionName));
            
            services.AddSingleton<IFileSystem, FileSystem>()
                .AddSingleton<IDatabasesCollection, DatabasesCollection>()
                .AddSingleton<IProgress<int>, ConsoleProgress>()
                .AddSqlServer()
                .AddMySql()
                .AddExecutable(options)
                .AddHostedService<ExecutableBackgroundService>();
        });
}

internal static class ExitCode
{
    public const int Ok = 0;
    public const int InvalidArguments = -1;
    public const int GenericError = -2;
}