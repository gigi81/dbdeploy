using System.IO.Abstractions.TestingHelpers;
using System.Text;

namespace Grillisoft.Tools.DatabaseDeploy.Tests;

/// <summary>
/// The one encoding the files of a deployment folder are allowed to be in.
/// </summary>
public class ScriptEncodingTests
{
    private const string ScriptPath = "/repo/db/TKT-001.Deploy.sql";
    private const string Script = "SELECT 'café'";

    [Test]
    public async Task Validate_WhenTheFileIsUtf8WithoutBOM_ReportsNothing()
    {
        var error = await Validate(new UTF8Encoding(false).GetBytes(Script));

        error.Should().BeNull();
    }

    [Test]
    public async Task Validate_WhenTheFileHasABOM_ReportsIt()
    {
        var error = await Validate(new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(Script)));

        error.Should().StartWith("BOM detected").And.Contain(ScriptPath);
    }

    /// <summary>
    /// The one nothing on disk gives away: no BOM to detect, and the bytes only turn into mojibake
    /// once the script reaches a database.
    /// </summary>
    [Test]
    public async Task Validate_WhenTheFileIsLatin1_ReportsIt()
    {
        var error = await Validate(Encoding.Latin1.GetBytes(Script));

        error.Should().Contain("is not UTF8").And.Contain("invalid byte sequence").And.Contain(ScriptPath);
    }

    /// <summary>
    /// An ASCII script saved as UTF-16 and stripped of its BOM is valid UTF-8, every other byte
    /// being a NUL, so the decoder alone would let it through.
    /// </summary>
    [Test]
    public async Task Validate_WhenTheFileIsUtf16WithoutBOM_ReportsIt()
    {
        var error = await Validate(new UnicodeEncoding(false, false).GetBytes("SELECT 1"));

        error.Should().Contain("is not UTF8").And.Contain("NUL").And.Contain(ScriptPath);
    }

    [Test]
    public async Task Validate_WhenTheFileEndsMidCharacter_ReportsIt()
    {
        //the last byte of the two the é is encoded as
        var error = await Validate(Encoding.UTF8.GetBytes("-- café").SkipLast(1));

        error.Should().Contain("is not UTF8").And.Contain(ScriptPath);
    }

    /// <summary>
    /// The chunks the file is read in must not cut a character in half, so a character landing on a
    /// boundary is not reported as a broken one.
    /// </summary>
    [Test]
    public async Task Validate_WhenTheFileIsLargerThanOneChunk_ReportsNothing()
    {
        var error = await Validate(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("-- café\n", 4096))));

        error.Should().BeNull();
    }

    [Test]
    public async Task Validate_WhenTheFileIsEmpty_ReportsNothing()
    {
        var error = await Validate([]);

        error.Should().BeNull();
    }

    [Test]
    public void IsUtf8NoBom_ShouldTellTheAllowedEncodingFromTheRest()
    {
        new UTF8Encoding(false).IsUtf8NoBom().Should().BeTrue("a different instance is still the same encoding");
        new UTF8Encoding(true).IsUtf8NoBom().Should().BeFalse();
        Encoding.Latin1.IsUtf8NoBom().Should().BeFalse();
        Encoding.Unicode.IsUtf8NoBom().Should().BeFalse();
    }

    private static async Task<string?> Validate(IEnumerable<byte> content)
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(ScriptPath, new MockFileData(content.ToArray()));

        return await ScriptEncoding.Validate(fileSystem.FileInfo.New(ScriptPath));
    }
}
