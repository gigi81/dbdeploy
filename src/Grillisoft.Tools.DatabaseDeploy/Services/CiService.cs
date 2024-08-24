using System.IO.Abstractions;
using System.Reflection;
using CliWrap;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Grillisoft.Tools.DatabaseDeploy.Services;

public class CiService : BaseService
{
    private readonly CiOptions _options;

    public CiService(
        CiOptions options,
        IDatabasesCollection databases,
        IFileSystem fileSystem,
        IOptions<GlobalSettings> globalOptions,
        ILogger<CiService> logger
    ) : base(databases, fileSystem, globalOptions, logger)
    {
        _options = options;
    }

    private string DefaultBranch => _globalSettings.Value.DefaultBranch;

    private string Branch => !string.IsNullOrWhiteSpace(_options.Branch)
        ? _options.Branch
        : this.DefaultBranch;

    public async override Task<int> Execute(CancellationToken cancellationToken)
    {
        var steps = this.GetSteps().ToArray();
        var current = 1;

        _logger.LogInformation("Running CI deployment for branch {Branch}", this.Branch);
        foreach (var step in steps)
        {
            _logger.LogInformation("Executing Step {CurrentStep}/{TotalSteps} ------------", current, steps.Length);
            var result = await step.ExecuteAsync(cancellationToken);
            _logger.LogInformation("Step {CurrentStep}/{TotalSteps} terminated with exit code {ExitCode}", current, steps.Length, result.ExitCode);

            if (result.ExitCode != 0)
                return result.ExitCode;

            current++;
        }

        return 0;
    }

    private IEnumerable<Command> GetSteps()
    {
        yield return ExecuteDbDeploy("deploy", this.DefaultBranch, _options.Create);
        if (this.Branch.EqualsIgnoreCase(this.DefaultBranch))
            yield break;

        yield return ExecuteDbDeploy("deploy", this.Branch, _options.Create);
        yield return ExecuteDbDeploy("rollback", this.Branch, false);
        yield return ExecuteDbDeploy("deploy", this.Branch, _options.Create);
    }

    private Command ExecuteDbDeploy(string verb, string branch, bool create = false)
    {
        return Cli.Wrap(ExecutablePath)
            .WithArguments(GetArguments(verb, branch, create))
            .WithStandardOutputPipe(PipeTarget.ToDelegate(Console.WriteLine))
            .WithStandardErrorPipe(PipeTarget.ToDelegate(Console.WriteLine))
            .WithWorkingDirectory(_options.Path);
    }

    private static IEnumerable<string> GetArguments(string verb, string branch, bool create)
    {
        IEnumerable<string> arguments = IsDll ? [DbDeployPath] : Array.Empty<string>();
        arguments = arguments.Concat([verb, "--path", ".", "-b", branch]);
        if (create)
            arguments = arguments.Concat(["--create"]);

        return arguments;
    }

    private static string DbDeployPath => Assembly.GetEntryAssembly()!.Location;

    private static bool IsDll => DbDeployPath.EndsWith(".dll", StringComparison.InvariantCultureIgnoreCase);

    private static string ExecutablePath => IsDll ? "dotnet" : DbDeployPath;
}