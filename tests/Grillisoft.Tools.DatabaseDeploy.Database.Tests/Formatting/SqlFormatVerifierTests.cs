using Grillisoft.Tools.DatabaseDeploy.Database.Formatting;

namespace Grillisoft.Tools.DatabaseDeploy.Database.Tests.Formatting;

/// <summary>
/// The verifier is what makes a re-flow formatter safe to point at a migration script, so it has to
/// be strict about content and blind to layout.
/// </summary>
public class SqlFormatVerifierTests
{
    private static string? Verify(string before, string after)
    {
        var lexer = new SqlLexer(new TestDialect(batchSeparator: "GO"));
        return SqlFormatVerifier.Verify(lexer.Tokenize(before), lexer.Tokenize(after));
    }

    [Test]
    public void Verify_WhenOnlyTheLayoutChanged_ShouldPass()
    {
        Verify("select a,b from t", "SELECT\n    a,\n    b\nFROM\n    t").Should().BeNull();
    }

    [Test]
    public void Verify_WhenAKeywordWasRecased_ShouldPass()
    {
        Verify("select 1", "SELECT 1").Should().BeNull();
    }

    [Test]
    public void Verify_WhenACommentWasReindented_ShouldPass()
    {
        Verify("/* a\nb */ select 1", "/* a\n    b */\nSELECT 1").Should().BeNull();
    }

    [Test]
    public void Verify_WhenABatchSeparatorWasRecased_ShouldPass()
    {
        Verify("select 1\ngo\n", "SELECT 1\nGO\n").Should().BeNull();
    }

    [Test]
    public void Verify_WhenAStatementWasLost_ShouldReportIt()
    {
        Verify("select 1; select 2;", "SELECT 1;")
            .Should().NotBeNull().And.Contain("lost");
    }

    [Test]
    public void Verify_WhenATokenAppeared_ShouldReportIt()
    {
        Verify("select 1", "SELECT 1, 2")
            .Should().NotBeNull().And.Contain("appeared");
    }

    /// <summary>
    /// Losing a comment is the failure mode that rules out a parse-and-reprint formatter, so it has
    /// to be caught wherever in the script it happens.
    /// </summary>
    [Test]
    public void Verify_WhenACommentWasDropped_ShouldReportIt()
    {
        Verify("-- note\nselect 1", "SELECT 1")
            .Should().NotBeNull().And.Contain("LineComment '-- note'");
    }

    /// <summary>
    /// An identifier is not a keyword, so a formatter is never allowed to change its case.
    /// </summary>
    [Test]
    public void Verify_WhenAnIdentifierWasRecased_ShouldReportIt()
    {
        Verify("select \"MyCol\" from t", "SELECT \"mycol\" FROM t")
            .Should().NotBeNull().And.Contain("changed");
    }

    /// <summary>Whitespace inside a literal is content, not layout.</summary>
    [Test]
    public void Verify_WhenALiteralChanged_ShouldReportIt()
    {
        Verify("select 'a  b'", "SELECT 'a b'")
            .Should().NotBeNull().And.Contain("changed");
    }

    [Test]
    public void Verify_WhenAnOperatorChanged_ShouldReportIt()
    {
        Verify("where a >= b", "WHERE a > b")
            .Should().NotBeNull().And.Contain("changed");
    }
}
