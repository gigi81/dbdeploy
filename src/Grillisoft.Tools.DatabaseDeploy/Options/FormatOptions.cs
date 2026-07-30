using CommandLine;
// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable PropertyCanBeMadeInitOnly.Global
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global

namespace Grillisoft.Tools.DatabaseDeploy.Options;

[Verb("format", HelpText = "Formats SQL scripts")]
public sealed class FormatOptions : OptionsBase
{
    [Option(shortName: 'i', longName: "include",
        HelpText = "Glob(s) of scripts to format, relative to the path, for example \"**/*.sql\". " +
                   "Given one, every matching file is formatted instead of the deploy and rollback " +
                   "scripts of each branch, no database is contacted, and nothing is skipped. " +
                   "Separate several globs with spaces after the one flag: -i \"**/*.sql\" \"setup/*.ddl\". " +
                   "Only *, ** and ? are supported: a character class such as [Ii] matches nothing.")]
    public IEnumerable<string> Include { get; set; } = [];

    [Option(shortName: 'e', longName: "exclude",
        HelpText = "Glob(s) to leave out of --include, several separated by spaces after the one flag. " +
                   "Same syntax as --include, so write \"_Init*\" \"_init*\" rather than \"_[Ii]nit*\".")]
    public IEnumerable<string> Exclude { get; set; } = [];

    [Option(longName: "provider",
        HelpText = "Dialect to format with when it cannot be told from the folder layout, " +
                   "for example sqlServer, oracle, mySql or postgreSql.")]
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Globs select the files, so there is no branch structure to read and no database to ask.
    /// </summary>
    public bool IsDirectoryMode => Include.Any();
}
