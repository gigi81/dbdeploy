using CommandLine;

namespace Grillisoft.Tools.DatabaseDeploy.Options;

[Verb("ci", HelpText = "Runs a CI test where the migrations are deployed, rolled back and deployed again")]
public sealed class CiOptions : OptionsBase
{
    [Option(shortName: 'c', longName: "create", HelpText = "Enable creation of the database if the database does not exists")]
    public bool Create { get; set; }
}