using CommandLine;

namespace Grillisoft.Tools.DatabaseDeploy.Options;

public class OptionsBase
{
    [Option(shortName: 'b', longName: "branch", HelpText = "Name of branch to deploy (defaults to 'main')")]
    public string Branch { get; set; } = "main";

    [Option(shortName: 'p', longName: "path", HelpText = "Path of directory containing databases scripts (defaults to current directory)")]
    public string Path { get; set; } = ".";

    [Option(shortName: 'd', longName: "dryrun", HelpText = "Enable to test what scripts needs to be deployed without actually running them")]
    public bool DryRun { get; set; } = false;
}