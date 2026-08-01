using System.Text;

namespace Grillisoft.Tools.DatabaseDeploy.Tests;

/// <summary>
/// Which files of the layout have their encoding checked. What counts as a bad one is
/// <see cref="ScriptEncoding"/>'s business, and is covered by <see cref="ScriptEncodingTests"/>.
/// </summary>
public class LayoutValidatorEncodingTests
{
    private static readonly string ScriptPath =
        $"{SampleBranches.RootPath}Database01{Path.DirectorySeparatorChar}TKT-001.SampleDescription.Deploy.sql";

    [Test]
    public async Task Validate_WhenEveryFileIsUtf8WithoutBOM_ReportsNoError()
    {
        var (errors, _) = await Validate(new UTF8Encoding(false).GetBytes("SELECT 'café'"));

        errors.Should().BeEmpty();
    }

    /// <summary>The scripts of the database folders are checked, whatever the encoding is wrong in.</summary>
    [Test]
    public async Task Validate_WhenAScriptIsNotUtf8_ReportsAnError()
    {
        var (errors, path) = await Validate(Encoding.Latin1.GetBytes("SELECT 'café'"));

        errors.Should().ContainSingle().Which.Should().Contain("is not UTF8").And.Contain(path);
    }

    /// <returns>
    /// The errors, along with the path the script ended up at: the file system decides that, and
    /// spells it the windows way when the tests run on windows.
    /// </returns>
    private static async Task<(List<string> Errors, string Path)> Validate(byte[] content)
    {
        var fileSystem = SampleBranches.CreateFileSystem();
        fileSystem.File.WriteAllBytes(ScriptPath, content);

        var reader = new BranchesReader(
            fileSystem.DirectoryInfo.New(SampleBranches.RootPath),
            SampleBranches.GlobalSettings);

        await reader.Load();

        return (await SampleBranches.Validate(reader), fileSystem.FileInfo.New(ScriptPath).FullName);
    }
}
