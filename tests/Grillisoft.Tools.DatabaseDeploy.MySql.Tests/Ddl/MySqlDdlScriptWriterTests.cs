using System.IO.Abstractions.TestingHelpers;
using System.Text;
using Grillisoft.Tools.DatabaseDeploy.MySql.Ddl;

namespace Grillisoft.Tools.DatabaseDeploy.MySql.Tests.Ddl;

/// <summary>
/// The writer and <see cref="MySqlScriptParser"/> are two halves of one contract, so most of these
/// assert on what the parser reads back rather than on the layout.
/// </summary>
public class MySqlDdlScriptWriterTests
{
    private static async Task<string> Write(params string[] statements)
    {
        using var stream = new MemoryStream();

        await using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
        {
            var ddl = new MySqlDdlScriptWriter(writer);
            foreach (var statement in statements)
                await ddl.WriteStatement(statement);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static async Task<List<string>> Parse(string script)
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile("/scripts/init.sql", new MockFileData(script));

        var parsed = new List<string>();
        await foreach (var statement in new MySqlScriptParser()
                           .Parse(fileSystem.FileInfo.New("/scripts/init.sql"), CancellationToken.None))
        {
            parsed.Add(statement);
        }

        return parsed;
    }

    [Test]
    public async Task WriteStatement_WhenTheStatementIsOneLine_ShouldJustTerminateIt()
    {
        var script = await Write("CREATE TABLE `t1` (`id` int)");

        script.Should().NotContain("DELIMITER");
        (await Parse(script)).Should().ContainSingle()
            .Which.Trim().Should().Be("CREATE TABLE `t1` (`id` int)");
    }

    /// <summary>
    /// A routine body ends lines with semicolons, so written plainly the parser would cut it in
    /// half at the first one.
    /// </summary>
    [Test]
    public async Task WriteStatement_WhenTheStatementHoldsSemicolons_ShouldWrapItInADelimiter()
    {
        const string procedure = """
            CREATE PROCEDURE `p`()
            BEGIN
              SELECT 1;
              SELECT 2;
            END
            """;

        var script = await Write(procedure);

        script.Should().Contain("DELIMITER $$").And.Contain("DELIMITER ;");
        (await Parse(script)).Should().ContainSingle()
            .Which.Should().Contain("SELECT 1;").And.Contain("SELECT 2;").And.Contain("END");
    }

    /// <summary>
    /// The delimiter is written on the line after the statement, so a statement whose own last line
    /// ends with the candidate would be cut short by it.
    /// </summary>
    [Test]
    public void ChooseDelimiter_ShouldNotPickOneThatEndsALineOfTheStatement()
    {
        const string statement = """
            CREATE PROCEDURE `p`()
            BEGIN
              SET @x = 1;
            END $$
            """;

        MySqlDdlScriptWriter.ChooseDelimiter(statement).Should().Be("//");
    }

    [Test]
    public void ChooseDelimiter_WhenOnlyTheLastLineEndsWithASemicolon_ShouldNotWrap()
    {
        MySqlDdlScriptWriter.ChooseDelimiter("SELECT 1;").Should().BeNull();
    }

    /// <summary>
    /// Both shapes have to survive one after the other: the delimiter is restored to a semicolon
    /// after a wrapped statement, so the next plain one still parses.
    /// </summary>
    [Test]
    public async Task WriteStatement_ShouldRestoreTheDelimiterForTheNextStatement()
    {
        var script = await Write(
            "CREATE TABLE `t1` (`id` int)",
            "CREATE FUNCTION `f`() RETURNS int\nBEGIN\n  RETURN 1;\nEND",
            "CREATE TABLE `t2` (`id` int)");

        var parsed = await Parse(script);

        parsed.Should().HaveCount(3);
        parsed[0].Trim().Should().Be("CREATE TABLE `t1` (`id` int)");
        parsed[1].Should().Contain("RETURN 1;");
        parsed[2].Trim().Should().Be("CREATE TABLE `t2` (`id` int)");
    }
}
