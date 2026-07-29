using System.Text;
using AwesomeAssertions;
using Grillisoft.Tools.DatabaseDeploy.SqlServer.Ddl;
using Xunit;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer.Tests.Ddl;

public class StreamWriterExtensionsTests
{
    private static async Task<string[]> Write(Func<StreamWriter, Task> write)
    {
        using var stream = new MemoryStream();

        await using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
        {
            await write(writer);
        }

        return Encoding.UTF8.GetString(stream.ToArray()).Split(Environment.NewLine);
    }

    [Fact]
    public async Task WriteComment_ShouldPrefixTheLine()
    {
        var lines = await Write(writer => writer.WriteComment("Database HR - 3 object(s)"));

        lines.Should().StartWith(["-- Database HR - 3 object(s)"]);
    }

    /// <summary>
    /// The failure notes written into the script carry whatever the server said, which can hold a
    /// line break. Every line has to be commented, or the rest of the message is read as T-SQL.
    /// </summary>
    [Fact]
    public async Task WriteComment_WhenTheCommentSpansLines_ShouldPrefixEveryLine()
    {
        var lines = await Write(writer => writer.WriteComment("first\nsecond\nthird"));

        lines.Should().StartWith(["-- first", "-- second", "-- third"]);
    }

    /// <summary>A message that came back with Windows line endings must not leave a stray return.</summary>
    [Fact]
    public async Task WriteComment_ShouldNotLeaveACarriageReturnBehind()
    {
        var lines = await Write(writer => writer.WriteComment("first\r\nsecond"));

        lines.Should().StartWith(["-- first", "-- second"]);
    }

    [Fact]
    public async Task WriteStatement_ShouldFollowTheStatementWithATerminatorAndABlankLine()
    {
        var lines = await Write(writer => writer.WriteStatement("CREATE TABLE [dbo].[T1] ([Id] int)"));

        lines.Should().StartWith(["CREATE TABLE [dbo].[T1] ([Id] int)", "GO", ""]);
    }

    /// <summary>
    /// SMO hands back batches with leading and trailing whitespace; a terminator has to sit on a
    /// line of its own, so the statement cannot end with a blank line of its own.
    /// </summary>
    [Fact]
    public async Task WriteStatement_ShouldTrimTheStatement()
    {
        var lines = await Write(writer => writer.WriteStatement("\n  SET ANSI_NULLS ON  \n\n"));

        lines.Should().StartWith(["SET ANSI_NULLS ON", "GO", ""]);
    }

    [Fact]
    public async Task WriteStatement_WhenTheStatementSpansLines_ShouldKeepItWhole()
    {
        var lines = await Write(writer => writer.WriteStatement("CREATE TABLE [T1](\n\t[Id] [int] NOT NULL\n)"));

        lines.Should().StartWith(["CREATE TABLE [T1](", "\t[Id] [int] NOT NULL", ")", "GO", ""]);
    }
}
