using CommandLine;
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Grillisoft.Tools.DatabaseDeploy.Options;

[Verb("deploy", HelpText = "Runs a set of deploy scripts to one or more databases")]
public sealed class DeployOptions : BranchOptions
{
    [Option(shortName: 't', longName: "test", Default = false, HelpText = "Enable deployments of test scripts")]
    public bool Test { get; set; } = false;

    [Option(shortName: 'c', longName: "create", Default = false, HelpText = "Enable creation of the database if the database does not exists")]
    public bool Create { get; set; }
}