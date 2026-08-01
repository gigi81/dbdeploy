using System.IO.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.Contracts;

/// <summary>
/// A configured hook script of a database. The script is looked up in the database folder first
/// and in the root folder after, so that databases can share one script and still override it.
/// </summary>
public sealed record HookScript(string Database, DatabaseHook Hook, string Name, IDirectoryInfo Root)
{
    /// <summary>
    /// The script inside the database folder, the one that takes precedence.
    /// </summary>
    public IFileInfo DatabaseFile => this.Root.SubDirectory(this.Database).File($"{Name}.sql");

    /// <summary>
    /// The script shared by every database, in the root folder.
    /// </summary>
    public IFileInfo RootFile => this.Root.File($"{Name}.sql");

    /// <summary>
    /// Where the script is looked up, in order of precedence.
    /// </summary>
    public IEnumerable<IFileInfo> Candidates
    {
        get
        {
            yield return this.DatabaseFile;
            yield return this.RootFile;
        }
    }

    /// <summary>
    /// The script to run, or null when none of the <see cref="Candidates"/> exists.
    /// </summary>
    public IFileInfo? File => this.Candidates.FirstOrDefault(f => f.Exists);

    /// <summary>
    /// How a configured but missing script is reported, both by the validation and by the run.
    /// </summary>
    public string NotFoundMessage =>
        $"Could not find {Hook} script for database {Database}: expected {DatabaseFile.FullName} or {RootFile.FullName}";
}
