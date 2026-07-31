using System.IO.Abstractions;
using System.Text;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Soenneker.Extensions.Enumerable.String;

namespace Grillisoft.Tools.DatabaseDeploy;

/// <summary>
/// Changes the csv branch files of a directory. Every write to them goes through this class, so
/// a run that must not touch them - a dry run - simply never creates one.
/// </summary>
public class BranchesWriter
{
    private readonly IDirectoryInfo _directory;
    private readonly GlobalSettings _globalSettings;

    public BranchesWriter(IDirectoryInfo directory, GlobalSettings globalSettings)
    {
        _directory = directory;
        _globalSettings = globalSettings;
    }

    /// <summary>
    /// Appends the steps to the default branch file, deletes the given branch files, and drops any
    /// '@include' of them left in the branch files that remain. Returns the names of the branches
    /// that were released.
    /// </summary>
    /// <param name="steps">The steps of the released branch, in deploy order.</param>
    /// <param name="files">
    /// The files of the released branch, as read by <see cref="BranchesReader.GetBranchFiles"/>.
    /// </param>
    public async Task<IReadOnlyCollection<string>> Release(
        IReadOnlyCollection<Step> steps,
        IReadOnlyCollection<IFileInfo> files,
        CancellationToken cancellationToken = default)
    {
        var mainFile = BranchFiles.GetFile(_globalSettings.DefaultBranch, _directory);
        var released = files.Select(f => f.Name).ToHashSetIgnoreCase();

        if (released.Contains(mainFile.Name))
            throw new ArgumentException($"The default branch {_globalSettings.DefaultBranch} cannot be released", nameof(files));

        //the steps are appended first so that a failure halfway through leaves them duplicated
        //rather than lost
        await AppendSteps(mainFile, steps, cancellationToken);

        foreach (var file in files)
            file.Delete();

        await RemoveIncludes(released, cancellationToken);

        return files.Select(BranchFiles.GetBranchName).ToArray();
    }

    private static async Task AppendSteps(IFileInfo file, IEnumerable<Step> steps, CancellationToken cancellationToken)
    {
        var content = await file.FileSystem.File.ReadAllTextAsync(file.FullName, cancellationToken);
        var newLine = GetNewLine(content);
        var builder = new StringBuilder(content);

        if (content.Length > 0 && !content.EndsWith('\n'))
            builder.Append(newLine);

        foreach (var step in steps)
            builder.Append(step.Database).Append(',').Append(step.Name).Append(newLine);

        await file.FileSystem.File.WriteAllTextAsync(file.FullName, builder.ToString(), cancellationToken);
    }

    private async Task RemoveIncludes(ISet<string> released, CancellationToken cancellationToken)
    {
        var files = BranchFiles.EnumerateFiles(_directory)
            .Where(f => !released.Contains(f.Name))
            .ToArray();

        foreach (var file in files)
            await RemoveIncludes(file, released, cancellationToken);
    }

    private static async Task RemoveIncludes(IFileInfo file, ISet<string> released, CancellationToken cancellationToken)
    {
        var content = await file.FileSystem.File.ReadAllTextAsync(file.FullName, cancellationToken);
        var directory = file.Directory ?? file.FileSystem.CurrentDirectory();
        var updated = string.Concat(SplitLines(content).Where(line => !IsIncludeOf(line, released, directory)));

        if (updated.Equals(content, StringComparison.Ordinal))
            return;

        await file.FileSystem.File.WriteAllTextAsync(file.FullName, updated, cancellationToken);
    }

    private static bool IsIncludeOf(string line, ISet<string> released, IDirectoryInfo directory)
    {
        var include = BranchFiles.GetIncludedBranch(line);
        return include != null && released.Contains(BranchFiles.GetFile(include, directory).Name);
    }

    /// <summary>
    /// Splits the content in lines, each one keeping its own terminator, so that the lines that
    /// are not removed are written back exactly as they were read.
    /// </summary>
    private static IEnumerable<string> SplitLines(string content)
    {
        var start = 0;

        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] != '\n')
                continue;

            yield return content.Substring(start, i - start + 1);
            start = i + 1;
        }

        if (start < content.Length)
            yield return content.Substring(start);
    }

    private static string GetNewLine(string content)
    {
        if (content.Contains("\r\n", StringComparison.Ordinal))
            return "\r\n";

        return content.Contains('\n') ? "\n" : Environment.NewLine;
    }
}
