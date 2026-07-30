using System.IO.Abstractions.TestingHelpers;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer.Tests;

public class SqlServerScriptParserTests
{
    private static async Task<List<string>> Parse(string script)
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile("/scripts/init.sql", new MockFileData(script));

        var parser = new SqlServerScriptParser();
        var commands = new List<string>();

        await foreach (var command in parser.Parse(fileSystem.FileInfo.New("/scripts/init.sql"), CancellationToken.None))
            commands.Add(command);

        return commands;
    }

    [Test]
    public async Task Parse_ShouldSplitBatchesOnTheirTerminator()
    {
        var commands = await Parse("""
            CREATE TABLE [T1] ([Id] int)
            GO

            CREATE TABLE [T2] ([Id] int)
            GO

            """);

        commands.Should().HaveCount(2);
        commands[0].Trim().Should().Be("CREATE TABLE [T1] ([Id] int)");
        commands[1].Trim().Should().Be("CREATE TABLE [T2] ([Id] int)");
    }

    /// <summary>
    /// An empty batch - a terminator with nothing before it, or the trailing one every generated
    /// script ends with - is rejected by the server, so it must never leave the parser.
    /// </summary>
    [Test]
    public async Task Parse_ShouldNotYieldEmptyBatches()
    {
        var commands = await Parse("""
            GO

            CREATE TABLE [T1] ([Id] int)
            GO
            GO


            """);

        commands.Should().ContainSingle()
                .Which.Trim().Should().Be("CREATE TABLE [T1] ([Id] int)");
    }

    /// <summary>The header dbdeploy writes at the top of a generated script is not a batch.</summary>
    [Test]
    public async Task Parse_ShouldKeepCommentsWithTheBatchTheyBelongTo()
    {
        var commands = await Parse("""
            -- ==========================
            -- Database HR - 1 object(s)
            -- ==========================

            CREATE TABLE [T1] ([Id] int)
            GO

            """);

        commands.Should().ContainSingle()
                .Which.Should().Contain("-- Database HR").And.Contain("CREATE TABLE [T1]");
    }

    [Test]
    public async Task Parse_ShouldIgnoreTheCaseAndTheIndentationOfTheTerminator()
    {
        var commands = await Parse("""
            SET ANSI_NULLS ON
              go
            SET QUOTED_IDENTIFIER ON
            Go

            """);

        commands.Should().HaveCount(2);
        commands[0].Trim().Should().Be("SET ANSI_NULLS ON");
        commands[1].Trim().Should().Be("SET QUOTED_IDENTIFIER ON");
    }

    /// <summary>
    /// A batch that is never terminated is still a batch: a hand written script does not have to end
    /// with a terminator.
    /// </summary>
    [Test]
    public async Task Parse_ShouldYieldTheLastBatchWithoutATerminator()
    {
        var commands = await Parse("CREATE TABLE [T1] ([Id] int)\n");

        commands.Should().ContainSingle()
                .Which.Trim().Should().Be("CREATE TABLE [T1] ([Id] int)");
    }

    [Test]
    public async Task Parse_WhenTheWordAppearsInsideAStatement_ShouldNotSplit()
    {
        var commands = await Parse("""
            INSERT INTO [Log] ([Message]) VALUES (N'this one has to GO through in one piece')
            GO

            """);

        commands.Should().ContainSingle()
                .Which.Should().Contain("GO through in one piece");
    }
}
