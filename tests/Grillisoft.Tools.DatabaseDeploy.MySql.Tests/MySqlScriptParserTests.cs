using System.IO.Abstractions.TestingHelpers;

namespace Grillisoft.Tools.DatabaseDeploy.MySql.Tests;

public class MySqlScriptParserTests
{
    private static async Task<List<string>> Parse(string script)
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile("/scripts/init.sql", new MockFileData(script));

        var parser = new MySqlScriptParser();
        var commands = new List<string>();

        await foreach (var command in parser.Parse(fileSystem.FileInfo.New("/scripts/init.sql"), CancellationToken.None))
            commands.Add(command);

        return commands;
    }

    [Test]
    public async Task Parse_ShouldSplitStatementsOnTheirDelimiter()
    {
        var commands = await Parse("""
            CREATE TABLE `t1` (`id` int);
            CREATE TABLE `t2` (`id` int);

            """);

        commands.Should().HaveCount(2);
        commands[0].Trim().Should().Be("CREATE TABLE `t1` (`id` int)");
        commands[1].Trim().Should().Be("CREATE TABLE `t2` (`id` int)");
    }

    /// <summary>
    /// A routine body holds its own semicolons, so the generator wraps it in a delimiter of its own.
    /// The lines announcing the delimiter are the parser's business and never reach the server.
    /// </summary>
    [Test]
    [Arguments("$$")]
    [Arguments(";;")]
    [Arguments("//")]
    public async Task Parse_ShouldHonourACustomDelimiter(string delimiter)
    {
        var commands = await Parse($"""
            CREATE TABLE `t1` (`id` int);

            DELIMITER {delimiter}
            CREATE PROCEDURE `p`()
            BEGIN
              SELECT 1;
              SELECT 2;
            END
            {delimiter}
            DELIMITER ;

            CREATE TABLE `t2` (`id` int);

            """);

        commands.Should().HaveCount(3);
        commands[1].Should().Contain("CREATE PROCEDURE `p`()")
                   .And.Contain("SELECT 1;")
                   .And.Contain("SELECT 2;")
                   .And.NotContain("DELIMITER");
        commands[2].Trim().Should().Be("CREATE TABLE `t2` (`id` int)");
    }

    /// <summary>
    /// The footer a generated script ends with is nothing but comments. Handed to the server it
    /// comes back as ER_EMPTY_QUERY, so it must never leave the parser.
    /// </summary>
    [Test]
    public async Task Parse_ShouldNotYieldATrailingCommentBlock()
    {
        var commands = await Parse("""
            CREATE TABLE `t1` (`id` int);

            -- ==========================
            -- !! `t2`---TABLE could not be scripted: nope
            -- ==========================

            """);

        commands.Should().ContainSingle()
                .Which.Trim().Should().Be("CREATE TABLE `t1` (`id` int)");
    }

    [Test]
    public async Task Parse_ShouldNotYieldAScriptThatIsOnlyComments()
    {
        var commands = await Parse("""
            # nothing to see here
            -- nor here

            """);

        commands.Should().BeEmpty();
    }

    /// <summary>The header dbdeploy writes at the top of a generated script is not a statement.</summary>
    [Test]
    public async Task Parse_ShouldKeepCommentsWithTheStatementTheyBelongTo()
    {
        var commands = await Parse("""
            -- ==========================
            -- Database sakila - 1 object(s)
            -- ==========================

            CREATE TABLE `t1` (`id` int);

            """);

        commands.Should().ContainSingle()
                .Which.Should().Contain("-- Database sakila").And.Contain("CREATE TABLE `t1`");
    }

    /// <summary>
    /// A bare <c>--</c> with no space after it is the subtraction operator, not a comment, so a
    /// statement made of one is a statement.
    /// </summary>
    [Test]
    public async Task Parse_ShouldNotMistakeTheMinusOperatorForAComment()
    {
        var commands = await Parse("SELECT 1 --1;\n");

        commands.Should().ContainSingle()
                .Which.Trim().Should().Be("SELECT 1 --1");
    }

    /// <summary>
    /// A statement that is never terminated is still a statement: a hand written script does not
    /// have to end with a delimiter.
    /// </summary>
    [Test]
    public async Task Parse_ShouldYieldTheLastStatementWithoutADelimiter()
    {
        var commands = await Parse("CREATE TABLE `t1` (`id` int)\n");

        commands.Should().ContainSingle()
                .Which.Trim().Should().Be("CREATE TABLE `t1` (`id` int)");
    }
}
