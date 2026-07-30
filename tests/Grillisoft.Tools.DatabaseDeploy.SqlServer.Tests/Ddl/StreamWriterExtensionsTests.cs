using System.Text;
using Grillisoft.Tools.DatabaseDeploy.SqlServer.Ddl;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer.Tests.Ddl;

public class StreamWriterExtensionsTests
{
    /// <summary>
    /// The lines of the written output, however they were broken. A statement keeps whatever line
    /// endings the server sent, while the writer ends each line it adds with
    /// <see cref="Environment.NewLine"/>, so on Windows one piece of output holds both kinds. The
    /// order of the separators matters: "\r\n" has to be tried before "\n".
    /// </summary>
    private static async Task<string[]> Write(Func<StreamWriter, Task> write, string? newLine = null)
    {
        using var stream = new MemoryStream();

        await using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
        {
            if (newLine is not null)
                writer.NewLine = newLine;

            await write(writer);
        }

        return Encoding.UTF8.GetString(stream.ToArray()).Split(["\r\n", "\n"], StringSplitOptions.None);
    }

    [Test]
    public async Task WriteComment_ShouldPrefixTheLine()
    {
        var lines = await Write(writer => writer.WriteComment("Database HR - 3 object(s)"));

        lines.Should().StartWith(["-- Database HR - 3 object(s)"]);
    }

    /// <summary>
    /// The failure notes written into the script carry whatever the server said, which can hold a
    /// line break. Every line has to be commented, or the rest of the message is read as T-SQL.
    /// </summary>
    [Test]
    public async Task WriteComment_WhenTheCommentSpansLines_ShouldPrefixEveryLine()
    {
        var lines = await Write(writer => writer.WriteComment("first\nsecond\nthird"));

        lines.Should().StartWith(["-- first", "-- second", "-- third"]);
    }

    /// <summary>A message that came back with Windows line endings must not leave a stray return.</summary>
    [Test]
    public async Task WriteComment_ShouldNotLeaveACarriageReturnBehind()
    {
        var lines = await Write(writer => writer.WriteComment("first\r\nsecond"));

        lines.Should().StartWith(["-- first", "-- second"]);
    }

    [Test]
    public async Task WriteStatement_ShouldFollowTheStatementWithATerminatorAndABlankLine()
    {
        var lines = await Write(writer => writer.WriteStatement("CREATE TABLE [dbo].[T1] ([Id] int)"));

        lines.Should().StartWith(["CREATE TABLE [dbo].[T1] ([Id] int)", "GO", ""]);
    }

    /// <summary>
    /// SMO hands back batches with leading and trailing whitespace; a terminator has to sit on a
    /// line of its own, so the statement cannot end with a blank line of its own.
    /// </summary>
    [Test]
    public async Task WriteStatement_ShouldTrimTheStatement()
    {
        var lines = await Write(writer => writer.WriteStatement("\n  SET ANSI_NULLS ON  \n\n"));

        lines.Should().StartWith(["SET ANSI_NULLS ON", "GO", ""]);
    }

    /// <summary>
    /// A statement keeps the line endings the server sent it with, while the terminator the writer
    /// adds gets the platform's. Both are exercised here so that a run on either platform catches a
    /// change that only shows up on the other.
    /// </summary>
    [Test]
    [Arguments("\n")]
    [Arguments("\r\n")]
    public async Task WriteStatement_WhenTheStatementSpansLines_ShouldKeepItWhole(string newLine)
    {
        var lines = await Write(
            writer => writer.WriteStatement("CREATE TABLE [T1](\n\t[Id] [int] NOT NULL\n)"), newLine);

        lines.Should().StartWith(["CREATE TABLE [T1](", "\t[Id] [int] NOT NULL", ")", "GO", ""]);
    }
}
