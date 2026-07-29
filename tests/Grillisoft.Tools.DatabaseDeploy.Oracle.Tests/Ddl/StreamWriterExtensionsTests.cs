using System.Text;
using AwesomeAssertions;
using Grillisoft.Tools.DatabaseDeploy.Oracle.Ddl;
using Xunit;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle.Tests.Ddl;

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

    /// <summary>
    /// SQL*Plus REM rather than a double dash, because that is what
    /// <see cref="OracleScriptParser"/> drops instead of sending to the server.
    /// </summary>
    [Fact]
    public async Task WriteComment_ShouldPrefixTheLineWithRem()
    {
        var lines = await Write(writer => writer.WriteComment("Schema HR - 3 object(s)"));

        lines.Should().StartWith(["REM Schema HR - 3 object(s)"]);
    }

    /// <summary>
    /// The failure notes written into the script carry whatever the server said, which can hold a
    /// line break. Every line has to be commented, or the rest of the message is read as PL/SQL.
    /// </summary>
    [Fact]
    public async Task WriteComment_WhenTheCommentSpansLines_ShouldPrefixEveryLine()
    {
        var lines = await Write(writer => writer.WriteComment("first\nsecond\nthird"));

        lines.Should().StartWith(["REM first", "REM second", "REM third"]);
    }

    [Fact]
    public async Task WriteComment_ShouldNotLeaveACarriageReturnBehind()
    {
        var lines = await Write(writer => writer.WriteComment("first\r\nsecond"));

        lines.Should().StartWith(["REM first", "REM second"]);
    }

    [Fact]
    public async Task WriteStatement_ShouldFollowTheStatementWithATerminatorAndABlankLine()
    {
        var lines = await Write(writer => writer.WriteStatement("CREATE TABLE \"T1\" (\"ID\" NUMBER)"));

        lines.Should().StartWith(["CREATE TABLE \"T1\" (\"ID\" NUMBER)", "/", ""]);
    }

    /// <summary>
    /// A lone slash only ends the statement when it sits on a line of its own, so the statement
    /// cannot bring trailing blank lines with it.
    /// </summary>
    [Fact]
    public async Task WriteStatement_ShouldTrimTheStatement()
    {
        var lines = await Write(writer => writer.WriteStatement("\n  CREATE SEQUENCE \"S1\"  \n\n"));

        lines.Should().StartWith(["CREATE SEQUENCE \"S1\"", "/", ""]);
    }

    /// <summary>A program unit keeps its internal line breaks, END included.</summary>
    [Fact]
    public async Task WriteStatement_WhenTheStatementSpansLines_ShouldKeepItWhole()
    {
        var lines = await Write(writer => writer.WriteStatement("CREATE PROCEDURE \"P1\" AS\nBEGIN\n  NULL;\nEND;"));

        lines.Should().StartWith(["CREATE PROCEDURE \"P1\" AS", "BEGIN", "  NULL;", "END;", "/", ""]);
    }
}
