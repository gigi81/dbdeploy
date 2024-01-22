using CommandLine;

namespace Grillisoft.Tools.DatabaseDeploy.Options;

[Verb("rollback", HelpText = "Runs a set of rollback scripts to one or more databases")]
public sealed class RollbackOptions : OptionsBase
{
}