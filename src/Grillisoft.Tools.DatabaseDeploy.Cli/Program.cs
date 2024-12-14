using System.IO.Abstractions;
using CommandLine;
using Grillisoft.Tools.DatabaseDeploy;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Cli;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

try
{
    var result = Parser.Default.ParseArguments<ValidateOptions, DeployOptions, RollbackOptions, CiOptions, GenerateOptions>(args);

    if (result.Errors.Any())
    {
        Environment.ExitCode = ExitCode.InvalidArguments;
        return;
    }

    await CreateHostBuilder((OptionsBase)result.Value, args).RunAsync();
}
catch (Exception ex)
{
    if (Environment.ExitCode == ExitCode.Ok)
        Environment.ExitCode = ExitCode.GenericError;

    Console.WriteLine(ex.Message);
}

static IHost CreateHostBuilder(OptionsBase options, string[] args)
{
    var builder = Host.CreateApplicationBuilder(args);
    var environmentName = builder.Environment.EnvironmentName;
    var configRoot = Path.GetFullPath(options.Path);

    builder.Configuration.AddJsonFile(Path.Combine(configRoot, "dbsettings.json"), optional: false);
    builder.Configuration.AddJsonFile(Path.Combine(configRoot, $"dbsettings.{environmentName}.json"), optional: true);

    builder.Services.AddSerilog(config =>
    {
        config.Enrich.FromLogContext()
            .ReadFrom.Configuration(builder.Configuration)
            .WriteTo.Console()
            .WriteTo.OpenTelemetry(options =>
            {
                options.ResourceAttributes = new Dictionary<string, object>()
                {
                    { "service.name", "dbdeploy" }
                };
            });
    });

    builder.Services.Configure<GlobalSettings>(
        builder.Configuration.GetSection(GlobalSettings.SectionName));

    builder.Services.AddSingleton<IFileSystem, FileSystem>()
        .AddSingleton<IDatabasesCollection, DatabasesCollection>()
        .AddSingleton<IProgress<int>, ConsoleProgress>()
        .AddAIGenerator()
        .AddSqlServer()
        .AddMySql()
        .AddOracle()
        .AddPostgreSql()
        .AddExecutable(options)
        .AddHostedService<ExecutableBackgroundService>();

    return builder.Build();
}