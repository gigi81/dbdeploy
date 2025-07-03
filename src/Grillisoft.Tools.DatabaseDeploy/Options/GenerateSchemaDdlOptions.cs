using CommandLine;
// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable ClassNeverInstantiated.Global

namespace Grillisoft.Tools.DatabaseDeploy.Options;

[Verb("generate-schema", HelpText = "Generates the full schema DDL.")]
public sealed class GenerateSchemaDdlOptions : OptionsBase
{
}