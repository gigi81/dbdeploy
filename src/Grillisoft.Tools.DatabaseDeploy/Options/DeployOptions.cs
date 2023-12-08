using CommandLine;

namespace Grillisoft.Tools.DatabaseDeploy.Options;

[Verb("deploy", HelpText = "Runs a set of deploy scripts to one or more databases")]
public sealed class DeployOptions
{
    public string Branch { get; set; } = "main";

    public string Path { get; set; } = "";
}