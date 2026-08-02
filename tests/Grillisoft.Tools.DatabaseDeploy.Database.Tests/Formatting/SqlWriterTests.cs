using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Database.Formatting;

namespace Grillisoft.Tools.DatabaseDeploy.Database.Tests.Formatting;

/// <summary>
/// The whitespace protocol the layout is written through: a space or a line break is only queued,
/// and what is written next decides what survives. <see cref="SqlEmitter"/> leans on it at every
/// token, so the rules are pinned here rather than only through a full format.
/// </summary>
public class SqlWriterTests
{
    private static SqlWriter Create(
        string indent = "  ",
        string newLine = "\n",
        bool insertFinalNewline = false,
        bool trimTrailingWhitespace = true) =>
        new(SqlFormatterOptions.Default with
        {
            Indent = indent,
            NewLine = newLine,
            InsertFinalNewline = insertFinalNewline,
            TrimTrailingWhitespace = trimTrailingWhitespace
        });

    // ------------------------------------------------------------ spaces

    [Test]
    public void Space_ShouldBeWrittenOnlyWhenSomethingFollows()
    {
        var writer = Create();

        writer.Write("SELECT");
        writer.Space();
        writer.Write("1");

        writer.Finish().Should().Be("SELECT 1");
    }

    [Test]
    public void Space_AskedForTwice_ShouldWriteOne()
    {
        var writer = Create();

        writer.Write("SELECT");
        writer.Space();
        writer.Space();
        writer.Write("1");

        writer.Finish().Should().Be("SELECT 1");
    }

    /// <summary>A space in front of a line break would only become trailing whitespace.</summary>
    [Test]
    public void NewLine_ShouldDropAQueuedSpace()
    {
        var writer = Create();

        writer.Write("SELECT");
        writer.Space();
        writer.NewLine();
        writer.Write("1");

        writer.Finish().Should().Be("SELECT\n1");
    }

    /// <summary>What a comma, a closing parenthesis or a member access needs.</summary>
    [Test]
    public void WriteTight_ShouldDropAQueuedSpace()
    {
        var writer = Create();

        writer.Write("a");
        writer.Space();
        writer.WriteTight(",");

        writer.Finish().Should().Be("a,");
    }

    [Test]
    public void SuppressSpace_ShouldDropAQueuedSpaceWithoutWriting()
    {
        var writer = Create();

        writer.Write("COUNT");
        writer.Space();
        writer.SuppressSpace();
        writer.Write("(");

        writer.Finish().Should().Be("COUNT(");
    }

    /// <summary>
    /// Writing nothing is not a write: the space stays queued for whatever comes next, which is what
    /// lets a caller write an empty phrase without losing the separator.
    /// </summary>
    [Test]
    public void Write_WithEmptyText_ShouldKeepAQueuedSpace()
    {
        var writer = Create();

        writer.Write("a");
        writer.Space();
        writer.Write(string.Empty);
        writer.Write("b");

        writer.Finish().Should().Be("a b");
    }

    // ------------------------------------------------------------ line breaks

    /// <summary>There is nothing above the first line to be separated from.</summary>
    [Test]
    public void NewLine_BeforeAnythingIsWritten_ShouldNotOpenWithABlankLine()
    {
        var writer = Create();

        writer.NewLine();
        writer.BlankLines(2);
        writer.Write("SELECT 1");

        writer.Finish().Should().Be("SELECT 1");
    }

    [Test]
    public void NewLine_AskedForTwice_ShouldBreakOnce()
    {
        var writer = Create();

        writer.Write("a");
        writer.NewLine();
        writer.NewLine();
        writer.Write("b");

        writer.Finish().Should().Be("a\nb");
    }

    [Test]
    public void BlankLines_ShouldWriteOneMoreBreakThanTheLinesAskedFor()
    {
        var writer = Create();

        writer.Write("a");
        writer.BlankLines(2);
        writer.Write("b");

        writer.Finish().Should().Be("a\n\n\nb");
    }

    /// <summary>The longest break asked for wins, whichever order they are asked in.</summary>
    [Test]
    public void BlankLines_ThenNewLine_ShouldKeepTheBlankLine()
    {
        var writer = Create();

        writer.Write("a");
        writer.BlankLines(1);
        writer.NewLine();
        writer.Write("b");

        writer.Finish().Should().Be("a\n\nb");
    }

    /// <summary>
    /// The one that lowers it again: a batch separator belongs directly under the batch it closes,
    /// however much of a gap the statement before it queued.
    /// </summary>
    [Test]
    public void ForceNewLine_ShouldDiscardABlankLineAlreadyQueued()
    {
        var writer = Create();

        writer.Write("SELECT 1;");
        writer.BlankLines(1);
        writer.ForceNewLine();
        writer.Write("GO");

        writer.Finish().Should().Be("SELECT 1;\nGO");
    }

    [Test]
    public void NewLine_ShouldUseTheConfiguredLineEnding()
    {
        var writer = Create(newLine: "\r\n");

        writer.Write("a");
        writer.NewLine();
        writer.Write("b");

        writer.Finish().Should().Be("a\r\nb");
    }

    // ------------------------------------------------------------ indenting

    [Test]
    public void Indent_ShouldBeWrittenAtTheStartOfALineOnly()
    {
        var writer = Create();
        writer.Indent = 2;

        writer.Write("a");
        writer.Space();
        writer.Write("b");
        writer.NewLine();
        writer.Write("c");

        writer.Finish().Should().Be("a b\n    c", "the first line had already started when the indent was set");
    }

    [Test]
    public void Indent_ShouldTakeEffectAtTheNextLineRatherThanImmediately()
    {
        var writer = Create();

        writer.Write("SELECT");
        writer.NewLine();
        writer.Indent = 1;
        writer.Write("1");
        writer.Indent = 0;
        writer.NewLine();
        writer.Write("FROM t");

        writer.Finish().Should().Be("SELECT\n  1\nFROM t");
    }

    [Test]
    public void IndentText_ShouldRepeatTheConfiguredIndent()
    {
        var writer = Create(indent: "\t");
        writer.Indent = 3;

        writer.IndentText.Should().Be("\t\t\t");
    }

    [Test]
    public void IndentText_AtLevelZero_ShouldBeEmpty()
    {
        Create().IndentText.Should().BeEmpty();
    }

    // ------------------------------------------------------------ multiline text

    [Test]
    public void WriteIndented_WithASingleLine_ShouldWriteItThrough()
    {
        var writer = Create();

        writer.Write("a");
        writer.NewLine();
        writer.Indent = 1;
        writer.WriteIndented("/* one line */");

        writer.Finish().Should().Be("a\n  /* one line */");
    }

    [Test]
    public void WriteIndented_ShouldPutTheLinesAfterTheFirstUnderTheCurrentIndent()
    {
        var writer = Create();

        writer.Write("SELECT 1");
        writer.NewLine();
        writer.Indent = 1;
        writer.WriteIndented("/* first\nsecond\nthird */");

        writer.Finish().Should().Be("SELECT 1\n  /* first\n  second\n  third */");
    }

    /// <summary>
    /// The text keeps the line endings it was written with: a comment carried over from a CRLF
    /// script is the author's, whatever the rest of the output uses.
    /// </summary>
    [Test]
    public void WriteIndented_ShouldKeepTheLineEndingsOfTheTextItIsGiven()
    {
        var writer = Create(newLine: "\n");
        writer.Indent = 1;

        writer.Write("a");
        writer.NewLine();
        writer.WriteIndented("/* first\r\nsecond */");

        writer.Finish().Should().Be("a\n  /* first\r\n  second */");
    }

    [Test]
    public void WriteIndented_WhenTrailingWhitespaceIsKept_ShouldStillLeftTrimBeforeIndenting()
    {
        var writer = Create(trimTrailingWhitespace: false);
        writer.Indent = 1;

        writer.Write("a");
        writer.NewLine();
        writer.WriteIndented("/* first\n      second   \n*/");

        writer.Finish().Should().Be("a\n  /* first\n  second   \n  */");
    }

    [Test]
    public void WriteIndented_ShouldTrimTheContinuationLinesWhenAskedTo()
    {
        var writer = Create(trimTrailingWhitespace: true);
        writer.Indent = 1;

        writer.Write("a");
        writer.NewLine();
        writer.WriteIndented("/* first\n      second   \n*/");

        writer.Finish().Should().Be("a\n  /* first\n  second\n  */");
    }

    [Test]
    public void WriteIndented_ShouldUseTheIndentInForceAtEachCall()
    {
        var writer = Create();

        writer.Indent = 1;
        writer.WriteIndented("/* a\nb */");
        writer.NewLine();
        writer.Indent = 2;
        writer.WriteIndented("/* c\nd */");

        writer.Finish().Should().Be("/* a\n  b */\n    /* c\n    d */");
    }

    // ------------------------------------------------------------ column

    [Test]
    public void Column_ShouldCountTheCharactersWrittenOnTheCurrentLine()
    {
        var writer = Create();

        writer.Write("SELECT");
        writer.Column.Should().Be(6);

        writer.Space();
        writer.Write("1");
        writer.Column.Should().Be(8);
    }

    /// <summary>
    /// With a break queued the line has not started yet, so the column is where it will start: the
    /// indent, not zero. <see cref="SqlEmitter"/> measures from here to decide what fits on a line.
    /// </summary>
    [Test]
    public void Column_WithALineBreakQueued_ShouldBeTheIndentOfTheLineToCome()
    {
        var writer = Create();

        writer.Write("SELECT");
        writer.NewLine();
        writer.Indent = 2;

        writer.Column.Should().Be(4);
    }

    // ------------------------------------------------------------ finishing

    [Test]
    public void Finish_ShouldTrimTheWhitespaceLeftQueuedAtTheEnd()
    {
        var writer = Create();

        writer.Write("SELECT 1");
        writer.BlankLines(2);

        writer.Finish().Should().Be("SELECT 1");
    }

    [Test]
    public void Finish_WhenAFinalNewlineIsAskedFor_ShouldEndWithOne()
    {
        var writer = Create(insertFinalNewline: true);

        writer.Write("SELECT 1");
        writer.NewLine();

        writer.Finish().Should().Be("SELECT 1\n");
    }

    /// <summary>An empty script stays empty rather than becoming a lone line break.</summary>
    [Test]
    public void Finish_WhenNothingWasWritten_ShouldBeEmpty()
    {
        var writer = Create(insertFinalNewline: true);

        writer.NewLine();

        writer.Finish().Should().BeEmpty();
    }
}
