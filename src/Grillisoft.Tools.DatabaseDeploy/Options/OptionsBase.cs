using CommandLine;
// ReSharper disable PropertyCanBeMadeInitOnly.Global

namespace Grillisoft.Tools.DatabaseDeploy.Options;

public abstract class OptionsBase
{
    [Option(shortName: 'p', longName: "path", HelpText = "Path of directory containing databases scripts (defaults to current directory)")]
    public string Path { get; set; } = ".";
}