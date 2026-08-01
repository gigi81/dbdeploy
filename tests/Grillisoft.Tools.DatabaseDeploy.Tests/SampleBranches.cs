using System.IO.Abstractions.TestingHelpers;
using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy.Tests;

/// <summary>
/// A branches directory with an include chain, used by the reader and the writer tests.
/// </summary>
public static class SampleBranches
{
    public static readonly string RootPath = OperatingSystem.IsWindows() ? "c:\\demo\\" : "/opt/demo/";
    public static readonly GlobalSettings GlobalSettings = new();

    /// <summary>
    /// For the tests that are about the branch layout and have no hook configured.
    /// </summary>
    public static readonly Func<string, DatabaseHooks> NoHooks = _ => DatabaseHooks.None;

    public static async Task<BranchesReader> Read(MockFileSystem filesystem)
    {
        var reader = new BranchesReader(filesystem.DirectoryInfo.New(RootPath), GlobalSettings);
        await reader.Load();
        (await Validate(reader)).Should().BeEmpty();
        return reader;
    }

    /// <summary>
    /// The errors of what a reader read, with no hook configured unless the test says otherwise.
    /// </summary>
    public static Task<List<string>> Validate(BranchesReader reader, Func<string, DatabaseHooks>? hooks = null)
    {
        return LayoutValidator.Validate(reader, GlobalSettings, hooks ?? NoHooks);
    }

    public static BranchesWriter CreateWriter(MockFileSystem filesystem)
    {
        return new BranchesWriter(filesystem.DirectoryInfo.New(RootPath), GlobalSettings);
    }

    public static string ReadFile(MockFileSystem filesystem, string name)
    {
        return filesystem.File.ReadAllText($"{RootPath}{name}");
    }

    public static MockFileSystem CreateFileSystem(string main = "Database01,_Init\nDatabase02,_Init\n")
    {
        var database01 = $"{RootPath}Database01{Path.DirectorySeparatorChar}";
        var database02 = $"{RootPath}Database02{Path.DirectorySeparatorChar}";

        return new MockFileSystem(new Dictionary<string, MockFileData>
        {
            { $"{RootPath}main.csv", new MockFileData(main) },
            //the release files of the examples have no final newline
            { $"{RootPath}release_1.1.csv", new MockFileData("Database01,TKT-001.SampleDescription") },
            { $"{RootPath}release_1.2.csv", new MockFileData("@include release_1.1\nDatabase02,TKT-002.SampleDescription\n") },
            { $"{RootPath}release_1.3.csv", new MockFileData("# a comment\n@include release/1.1\nDatabase01,TKT-003.SampleDescription\n") },
            { $"{database01}_Init.sql", new MockFileData("INIT Database01") },
            { $"{database01}TKT-001.SampleDescription.Deploy.sql", new MockFileData("DEPLOY") },
            { $"{database01}TKT-001.SampleDescription.Rollback.sql", new MockFileData("ROLLBACK") },
            { $"{database01}TKT-003.SampleDescription.Deploy.sql", new MockFileData("DEPLOY") },
            { $"{database01}TKT-003.SampleDescription.Rollback.sql", new MockFileData("ROLLBACK") },
            { $"{database02}_Init.sql", new MockFileData("INIT Database02") },
            { $"{database02}TKT-002.SampleDescription.Deploy.sql", new MockFileData("DEPLOY") },
            { $"{database02}TKT-002.SampleDescription.Rollback.sql", new MockFileData("ROLLBACK") },
        });
    }
}
