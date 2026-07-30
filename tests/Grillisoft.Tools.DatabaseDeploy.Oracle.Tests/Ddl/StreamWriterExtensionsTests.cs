using System.Text;
using Grillisoft.Tools.DatabaseDeploy.Oracle.Ddl;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle.Tests.Ddl;

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

    /// <summary>
    /// SQL*Plus REM rather than a double dash, because that is what
    /// <see cref="OracleScriptParser"/> drops instead of sending to the server.
    /// </summary>
    [Test]
    public async Task WriteComment_ShouldPrefixTheLineWithRem()
    {
        var lines = await Write(writer => writer.WriteComment("Schema HR - 3 object(s)"));

        lines.Should().StartWith(["REM Schema HR - 3 object(s)"]);
    }

    /// <summary>
    /// The failure notes written into the script carry whatever the server said, which can hold a
    /// line break. Every line has to be commented, or the rest of the message is read as PL/SQL.
    /// </summary>
    [Test]
    public async Task WriteComment_WhenTheCommentSpansLines_ShouldPrefixEveryLine()
    {
        var lines = await Write(writer => writer.WriteComment("first\nsecond\nthird"));

        lines.Should().StartWith(["REM first", "REM second", "REM third"]);
    }

    [Test]
    public async Task WriteComment_ShouldNotLeaveACarriageReturnBehind()
    {
        var lines = await Write(writer => writer.WriteComment("first\r\nsecond"));

        lines.Should().StartWith(["REM first", "REM second"]);
    }

    [Test]
    public async Task WriteStatement_ShouldFollowTheStatementWithATerminatorAndABlankLine()
    {
        var lines = await Write(writer => writer.WriteStatement("CREATE TABLE \"T1\" (\"ID\" NUMBER)"));

        lines.Should().StartWith(["CREATE TABLE \"T1\" (\"ID\" NUMBER)", "/", ""]);
    }

    /// <summary>
    /// A lone slash only ends the statement when it sits on a line of its own, so the statement
    /// cannot bring trailing blank lines with it.
    /// </summary>
    [Test]
    public async Task WriteStatement_ShouldTrimTheStatement()
    {
        var lines = await Write(writer => writer.WriteStatement("\n  CREATE SEQUENCE \"S1\"  \n\n"));

        lines.Should().StartWith(["CREATE SEQUENCE \"S1\"", "/", ""]);
    }

    /// <summary>
    /// A program unit keeps its internal line breaks, END included. It also keeps the line endings
    /// the server sent it with, while the terminator the writer adds gets the platform's, so both
    /// are exercised here: a run on either platform then catches a change that only shows up on the
    /// other.
    /// </summary>
    [Test]
    [Arguments("\n")]
    [Arguments("\r\n")]
    public async Task WriteStatement_WhenTheStatementSpansLines_ShouldKeepItWhole(string newLine)
    {
        var lines = await Write(
            writer => writer.WriteStatement("CREATE PROCEDURE \"P1\" AS\nBEGIN\n  NULL;\nEND;"), newLine);

        lines.Should().StartWith(["CREATE PROCEDURE \"P1\" AS", "BEGIN", "  NULL;", "END;", "/", ""]);
    }
}
