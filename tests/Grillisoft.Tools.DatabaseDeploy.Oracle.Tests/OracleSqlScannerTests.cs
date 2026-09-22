namespace Grillisoft.Tools.DatabaseDeploy.Oracle.Tests;

/// <summary>
/// What both Oracle splitters rest on: which characters of a script are code, and so which
/// terminators are real.
/// </summary>
public class OracleSqlScannerTests
{
    [Test]
    [Arguments("SELECT 1 FROM dual;", 18)]
    [Arguments("SELECT 1 FROM dual; /* the end */", 18)]
    [Arguments("SELECT 1 FROM dual; -- the end", 18)]
    [Arguments("SELECT 'a;' FROM dual", 20)]
    [Arguments("/* all comment */", -1)]
    [Arguments("-- all comment", -1)]
    [Arguments("   ", -1)]
    public void LastCodeIndex_ShouldIgnoreCommentsAndWhitespace(string text, int expected)
    {
        OracleSqlScanner.LastCodeIndex(text).Should().Be(expected);
    }

    /// <summary>
    /// The case the splitters used to get wrong: the slash closing a block comment is not a
    /// terminator.
    /// </summary>
    [Test]
    public void Scan_ShouldNotTakeTheSlashClosingABlockCommentForCode()
    {
        var scanner = new OracleSqlScanner();

        var (first, last) = scanner.Scan("SELECT \"ID\" /* the key */");

        first.Should().Be(0);
        last.Should().Be("SELECT \"ID\"".Length - 1);
        scanner.InCode.Should().BeTrue();
    }

    [Test]
    public void Scan_ShouldCarryABlockCommentAcrossLines()
    {
        var scanner = new OracleSqlScanner();

        scanner.Scan("NULL; /* starts here");
        scanner.InBlockComment.Should().BeTrue();

        scanner.Scan("/").Should().Be((-1, -1), "a slash inside a comment is part of the comment");
        scanner.Scan("it's still a comment; */ NULL;").Last.Should().Be("it's still a comment; */ NULL;".Length - 1);
        scanner.InCode.Should().BeTrue();
    }

    [Test]
    public void Scan_ShouldEndALineCommentWithItsLine()
    {
        var scanner = new OracleSqlScanner();

        scanner.Scan("NULL; -- don't /* open anything");
        scanner.InCode.Should().BeTrue();

        scanner.Scan("NULL;").Should().Be((0, 4));
    }

    [Test]
    public void Scan_ShouldCarryALiteralAcrossLines()
    {
        var scanner = new OracleSqlScanner();

        scanner.Scan("INSERT INTO t VALUES ('first line /*");
        scanner.InCode.Should().BeFalse();

        scanner.Scan("second line');");
        scanner.InCode.Should().BeTrue("the comment marker was inside the literal");
    }

    [Test]
    [Arguments("q'[it's /* not */ a comment]'")]
    [Arguments("Q'{it's -- not a comment}'")]
    [Arguments("q'(a ) b)'")]
    [Arguments("q'<a > b>'")]
    [Arguments("q'!it's!'")]
    [Arguments("nq'[it's]'")]
    [Arguments("N'it''s'")]
    public void Scan_ShouldReadQuoteLiteralsToTheirOwnEnd(string literal)
    {
        var scanner = new OracleSqlScanner();
        var text = $"SELECT {literal} FROM dual";

        scanner.Scan(text).Last.Should().Be(text.Length - 1);
        scanner.InCode.Should().BeTrue();
    }

    /// <summary>A q at the end of an identifier followed by a literal is not a q-quote.</summary>
    [Test]
    public void Scan_ShouldNotTakeTheTailOfAnIdentifierForAQuoteLiteral()
    {
        var scanner = new OracleSqlScanner();

        // seq'[' is the identifier seq followed by the literal '[' - which ends at the next quote
        scanner.Scan("SELECT seq'[' FROM dual");
        scanner.InCode.Should().BeTrue();
    }
}
