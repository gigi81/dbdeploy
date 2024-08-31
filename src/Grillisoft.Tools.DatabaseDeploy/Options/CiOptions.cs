using CommandLine;
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Grillisoft.Tools.DatabaseDeploy.Options;

[Verb("ci", HelpText = "Runs a CI test where the migrations are deployed, rolled back and deployed again")]
public sealed class CiOptions : BranchOptions
{
    [Option(shortName: 't', longName: "test", Default = false, HelpText = "Enable deployments of test scripts")]
    public bool Test { get; set; }

    [Option(shortName: 'c', longName: "create", Default = false, HelpText = "Enable creation of the database if the database does not exists")]
    public bool Create { get; set; }
}