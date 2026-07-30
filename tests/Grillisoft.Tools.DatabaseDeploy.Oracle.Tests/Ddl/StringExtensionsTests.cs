using Grillisoft.Tools.DatabaseDeploy.Oracle.Ddl;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle.Tests.Ddl;

public class StringExtensionsTests
{
    [Test]
    [Arguments("CUSTOMER", "\"CUSTOMER\"")]
    [Arguments("Mixed Case", "\"Mixed Case\"")]
    public void Quote_ShouldWrapTheNameInDoubleQuotes(string name, string expected)
    {
        name.Quote().Should().Be(expected);
    }

    [Test]
    public void Quote_WhenTheNameHoldsADoubleQuote_ShouldDoubleIt()
    {
        "we\"rd".Quote().Should().Be("\"we\"\"rd\"");
    }

    [Test]
    public void ToSqlLiteral_ShouldWrapTheValueInSingleQuotes()
    {
        "a comment".ToSqlLiteral().Should().Be("'a comment'");
    }

    /// <summary>
    /// Comments are written straight into the script rather than bound, so an apostrophe would end
    /// the literal early and leave the rest of the comment to be read as SQL.
    /// </summary>
    [Test]
    public void ToSqlLiteral_WhenTheValueHoldsAnApostrophe_ShouldDoubleIt()
    {
        "don't truncate".ToSqlLiteral().Should().Be("'don''t truncate'");
    }

    [Test]
    public void ToSqlLiteral_WhenTheValueIsEmpty_ShouldStillBeALiteral()
    {
        string.Empty.ToSqlLiteral().Should().Be("''");
    }
}
