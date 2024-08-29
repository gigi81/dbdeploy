using CommandLine;
// ReSharper disable PropertyCanBeMadeInitOnly.Global

namespace Grillisoft.Tools.DatabaseDeploy.Options;

public abstract class BranchOptions : OptionsBase
{
    [Option(shortName: 'b', longName: "branch", HelpText = "Name of branch to deploy (defaults to 'main' or the default branch name set in the global settings)")]
    public string? Branch { get; set; } = null;

    [Option(shortName: 'd', longName: "dryrun", HelpText = "Enable to test what scripts needs to be deployed without actually running them")]
    public bool DryRun { get; set; } = false;
}