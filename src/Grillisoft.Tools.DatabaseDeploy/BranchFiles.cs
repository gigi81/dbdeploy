using System.IO.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy;

/// <summary>
/// The conventions of the csv branch files: how a branch name maps to a file name and how an
/// '@include' line is written. Shared by <see cref="BranchesReader"/>, which parses them, and
/// <see cref="BranchesWriter"/>, which changes them.
/// </summary>
internal static class BranchFiles
{
    public const string Extension = "csv";
    public const string IncludeKeyword = "@include ";

    /// <summary>
    /// The file of a branch: 'release/1.1' is stored as 'release_1.1.csv'.
    /// </summary>
    public static IFileInfo GetFile(string branchName, IDirectoryInfo directory)
    {
        return directory.File($"{branchName.Replace('/', '_')}.{Extension}");
    }

    /// <summary>
    /// The branch of a file, the inverse of <see cref="GetFile"/>.
    /// </summary>
    public static string GetBranchName(IFileInfo file)
    {
        var name = file.Name;
        var index = name.LastIndexOf('.');
        if (index >= 0)
            name = name.Substring(0, index);

        return name.Replace('_', '/');
    }

    public static IEnumerable<IFileInfo> EnumerateFiles(IDirectoryInfo directory)
    {
        return directory.EnumerateFiles($"*.{Extension}", SearchOption.TopDirectoryOnly);
    }

    /// <summary>
    /// The branch included by the line, or null when the line is not an '@include'.
    /// </summary>
    public static string? GetIncludedBranch(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith('#') || !trimmed.StartsWith(IncludeKeyword, StringComparison.Ordinal))
            return null;

        return trimmed.Substring(IncludeKeyword.Length).Trim();
    }
}
