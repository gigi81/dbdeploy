using CommandLine;
// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable ClassNeverInstantiated.Global

namespace Grillisoft.Tools.DatabaseDeploy.Options;

[Verb("validate", HelpText = "Validates scripts filenames and csv files.")]
public sealed class ValidateOptions : OptionsBase
{
}