using CommandLine;

namespace Grillisoft.Tools.DatabaseDeploy.Options;

[Verb("deploy", HelpText = "Runs a set of deploy scripts to one or more databases")]
public sealed class DeployOptions : OptionsBase
{
    [Option(shortName: 't', longName: "test", HelpText = "Enable deployments of test scripts")]
    public bool Test { get; set; } = false;
}