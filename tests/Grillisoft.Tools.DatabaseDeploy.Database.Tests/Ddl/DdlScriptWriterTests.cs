using System.Text;
using Grillisoft.Tools.DatabaseDeploy.Database.Ddl;

namespace Grillisoft.Tools.DatabaseDeploy.Database.Tests.Ddl;

/// <summary>
/// The writer is shared, so every case runs for both the shapes in use: Oracle's <c>REM</c> and
/// lone slash, and the <c>--</c> plus <c>GO</c> or <c>;</c> everything else uses.
/// </summary>
public class DdlScriptWriterTests
{
    /// <summary>
    /// The lines of the written output, however they were broken. A statement keeps whatever line
    /// endings the server sent, while the writer ends each line it adds with
    /// <see cref="Environment.NewLine"/>, so on Windows one piece of output holds both kinds. The
    /// order of the separators matters: "\r\n" has to be tried before "\n".
    /// </summary>
    private static async Task<string[]> Write(
        string commentPrefix,
        string terminator,
        Func<DdlScriptWriter, Task> write,
        string? newLine = null)
    {
        using var stream = new MemoryStream();

        await using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
        {
            if (newLine is not null)
                writer.NewLine = newLine;

            await write(new DdlScriptWriter(writer, commentPrefix, terminator));
        }

        return Encoding.UTF8.GetString(stream.ToArray()).Split(["\r\n", "\n"], StringSplitOptions.None);
    }

    [Test]
    [Arguments("REM ", "/")]
    [Arguments("-- ", "GO")]
    public async Task WriteComment_ShouldPrefixTheLine(string prefix, string terminator)
    {
        var lines = await Write(prefix, terminator, writer => writer.WriteComment("Schema HR - 3 object(s)"));

        lines.Should().StartWith([prefix + "Schema HR - 3 object(s)"]);
    }

    /// <summary>
    /// The failure notes written into the script carry whatever the server said, which can hold a
    /// line break. Every line has to be commented, or the rest of the message is read as SQL.
    /// </summary>
    [Test]
    [Arguments("REM ", "/")]
    [Arguments("-- ", "GO")]
    public async Task WriteComment_WhenTheCommentSpansLines_ShouldPrefixEveryLine(string prefix, string terminator)
    {
        var lines = await Write(prefix, terminator, writer => writer.WriteComment("first\nsecond\nthird"));

        lines.Should().StartWith([prefix + "first", prefix + "second", prefix + "third"]);
    }

    [Test]
    [Arguments("REM ", "/")]
    [Arguments("-- ", "GO")]
    public async Task WriteComment_ShouldNotLeaveACarriageReturnBehind(string prefix, string terminator)
    {
        var lines = await Write(prefix, terminator, writer => writer.WriteComment("first\r\nsecond"));

        lines.Should().StartWith([prefix + "first", prefix + "second"]);
    }

    [Test]
    [Arguments("REM ", "/")]
    [Arguments("-- ", "GO")]
    public async Task WriteStatement_ShouldFollowTheStatementWithATerminatorAndABlankLine(
        string prefix, string terminator)
    {
        var lines = await Write(prefix, terminator,
            writer => writer.WriteStatement("CREATE TABLE \"T1\" (\"ID\" NUMBER)"));

        lines.Should().StartWith(["CREATE TABLE \"T1\" (\"ID\" NUMBER)", terminator, ""]);
    }

    /// <summary>
    /// A terminator only ends the statement when it sits on a line of its own, so the statement
    /// cannot bring trailing blank lines with it.
    /// </summary>
    [Test]
    [Arguments("REM ", "/")]
    [Arguments("-- ", "GO")]
    public async Task WriteStatement_ShouldTrimTheStatement(string prefix, string terminator)
    {
        var lines = await Write(prefix, terminator,
            writer => writer.WriteStatement("\n  CREATE SEQUENCE \"S1\"  \n\n"));

        lines.Should().StartWith(["CREATE SEQUENCE \"S1\"", terminator, ""]);
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
        var lines = await Write("REM ", "/",
            writer => writer.WriteStatement("CREATE PROCEDURE \"P1\" AS\nBEGIN\n  NULL;\nEND;"), newLine);

        lines.Should().StartWith(["CREATE PROCEDURE \"P1\" AS", "BEGIN", "  NULL;", "END;", "/", ""]);
    }

    [Test]
    public async Task WriteLine_ShouldWriteABlankLine()
    {
        var lines = await Write("-- ", "GO", async writer =>
        {
            await writer.WriteComment("header");
            await writer.WriteLine();
        });

        lines.Should().StartWith(["-- header", ""]);
    }
}
