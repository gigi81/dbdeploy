using System.IO.Abstractions;
using System.Reflection;
using CliWrap;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Soenneker.Extensions.String;

namespace Grillisoft.Tools.DatabaseDeploy.Services;

public class CiService : BaseService
{
    private const string DeployVerb = "deploy";
    private const string RollbackVerb = "rollback";

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
        yield return ExecuteDbDeploy(DeployVerb, this.DefaultBranch);
        if (this.Branch.EqualsIgnoreCase(this.DefaultBranch))
            yield break;

        yield return ExecuteDbDeploy(DeployVerb, this.Branch);
        yield return ExecuteDbDeploy(RollbackVerb, this.Branch);
        yield return ExecuteDbDeploy(DeployVerb, this.Branch);
    }

    private Command ExecuteDbDeploy(string verb, string branch)
    {
        return Cli.Wrap(ExecutablePath)
            .WithArguments(GetArguments(verb, branch))
            .WithStandardOutputPipe(PipeTarget.ToDelegate(Console.WriteLine))
            .WithStandardErrorPipe(PipeTarget.ToDelegate(Console.WriteLine))
            .WithWorkingDirectory(_options.Path);
    }

    private IEnumerable<string> GetArguments(string verb, string branch)
    {
        if (IsDll)
            yield return DbDeployPath;

        yield return verb;
        yield return "--path";
        //path is set by CLI working directory in ExecuteDbDeploy
        yield return ".";
        yield return "-b";
        yield return branch;

        if (_options.Create && verb == DeployVerb)
            yield return "--create";

        if (_options.Test && verb == DeployVerb)
            yield return "--test";
    }

    private static string DbDeployPath => Assembly.GetEntryAssembly()!.Location;

    private static bool IsDll => DbDeployPath.EndsWith(".dll", StringComparison.InvariantCultureIgnoreCase);

    private static string ExecutablePath => IsDll ? "dotnet" : DbDeployPath;
}