using Grillisoft.Tools.DatabaseDeploy.Database.Formatting;

namespace Grillisoft.Tools.DatabaseDeploy.Database.Tests.Formatting;

public class SqlLexerTests
{
    private static List<SqlToken> Tokenize(string sql, SqlDialect? dialect = null) =>
        new SqlLexer(dialect ?? new TestDialect()).Tokenize(sql);

    private static List<SqlToken> Significant(string sql, SqlDialect? dialect = null) =>
        Tokenize(sql, dialect).Where(t => !t.IsTrivia).ToList();

    /// <summary>
    /// The invariant the verifier depends on: nothing may be dropped or invented by the scan, so
    /// the tokens always reassemble into the exact input.
    /// </summary>
    [Test]
    [Arguments("select 1")]
    [Arguments("SELECT 'it''s' FROM t -- trailing\r\n")]
    [Arguments("/* block\n   comment */ SELECT \"quoted \"\" id\"")]
    [Arguments("a.b::text || @p1 + :bind + $2 + ?")]
    [Arguments("SELECT 1.5e-3, .5, 0.0 FROM t;\n\nGO\n")]
    [Arguments("   \t \n\n  ")]
    [Arguments("unterminated 'string")]
    [Arguments("unterminated /* comment")]
    public void Tokenize_ShouldBeLossless(string sql)
    {
        var tokens = Tokenize(sql);

        string.Concat(tokens.Select(t => t.Text)).Should().Be(sql);
    }

    [Test]
    public void Tokenize_ShouldReadAStringLiteralWithADoubledQuoteAsOneToken()
    {
        var tokens = Significant("'it''s here'");

        tokens.Should().ContainSingle();
        tokens[0].Kind.Should().Be(SqlTokenKind.StringLiteral);
        tokens[0].Text.Should().Be("'it''s here'");
    }

    [Test]
    public void Tokenize_ShouldReadABracketedIdentifierAsOneToken()
    {
        var dialect = new TestDialect(identifierQuotes: ['[', '"']);

        var tokens = Significant("[my table]", dialect);

        tokens.Should().ContainSingle();
        tokens[0].Kind.Should().Be(SqlTokenKind.QuotedIdentifier);
        tokens[0].Text.Should().Be("[my table]");
    }

    /// <summary>A semicolon inside a literal is not a statement terminator.</summary>
    [Test]
    public void Tokenize_ShouldNotFindATerminatorInsideALiteral()
    {
        var tokens = Significant("SELECT 'a;b'");

        tokens.Should().NotContain(t => t.Kind == SqlTokenKind.Terminator);
    }

    [Test]
    public void Tokenize_WhenTheDialectAllowsIt_ShouldReadAHashAsAComment()
    {
        var tokens = Significant("SELECT 1 # note", new TestDialect(hashComments: true));

        tokens.Should().ContainSingle(t => t.Kind == SqlTokenKind.LineComment && t.Text == "# note");
    }

    /// <summary>Without the dialect flag a hash starts an identifier, as it does for a temp table.</summary>
    [Test]
    public void Tokenize_WhenTheDialectDoesNotAllowIt_ShouldReadAHashAsAWord()
    {
        var tokens = Significant("SELECT * FROM #temp");

        tokens.Should().ContainSingle(t => t.Kind == SqlTokenKind.Word && t.Text == "#temp");
    }

    [Test]
    public void Tokenize_ShouldReadABatchSeparatorOnItsOwnLine()
    {
        var tokens = Significant("SELECT 1\ngo\n", new TestDialect(batchSeparator: "GO"));

        tokens.Should().ContainSingle(t => t.Kind == SqlTokenKind.BatchSeparator && t.Text == "go");
    }

    /// <summary>
    /// A separator is only a separator on a line of its own, so a column that happens to be called
    /// "go" survives.
    /// </summary>
    [Test]
    public void Tokenize_WhenTheSeparatorSharesALine_ShouldReadItAsAWord()
    {
        var tokens = Significant("SELECT go FROM t", new TestDialect(batchSeparator: "GO"));

        tokens.Should().NotContain(t => t.Kind == SqlTokenKind.BatchSeparator);
    }

    [Test]
    public void Tokenize_ShouldMarkTheFirstTokenOnALine()
    {
        var tokens = Tokenize("SELECT 1\n    FROM t");

        tokens.First(t => t.Text == "SELECT").StartsLine.Should().BeTrue();
        tokens.First(t => t.Text == "FROM").StartsLine.Should().BeTrue("only whitespace precedes it");
        tokens.First(t => t.Text == "t").StartsLine.Should().BeFalse();
    }

    [Test]
    public void Tokenize_WhenTheDialectAllowsIt_ShouldHonourABackslashEscape()
    {
        var tokens = Significant(@"'a\'b'", new TestDialect(backslashEscapes: true));

        tokens.Should().ContainSingle();
        tokens[0].Text.Should().Be(@"'a\'b'");
    }

    [Test]
    [Arguments("<>")]
    [Arguments("!=")]
    [Arguments(">=")]
    [Arguments("::")]
    [Arguments("||")]
    public void Tokenize_ShouldReadAMultiCharacterOperatorAsOneToken(string op)
    {
        var tokens = Significant($"a {op} b");

        tokens.Should().HaveCount(3);
        tokens[1].Should().Be(new SqlToken(SqlTokenKind.Operator, op));
    }
}
