using AwesomeAssertions;
using Grillisoft.Tools.DatabaseDeploy.Oracle.Ddl;
using Xunit;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle.Tests.Ddl;

public class StringExtensionsTests
{
    [Theory]
    [InlineData("CUSTOMER", "\"CUSTOMER\"")]
    [InlineData("Mixed Case", "\"Mixed Case\"")]
    public void Quote_ShouldWrapTheNameInDoubleQuotes(string name, string expected)
    {
        name.Quote().Should().Be(expected);
    }

    [Fact]
    public void Quote_WhenTheNameHoldsADoubleQuote_ShouldDoubleIt()
    {
        "we\"rd".Quote().Should().Be("\"we\"\"rd\"");
    }

    [Fact]
    public void ToSqlLiteral_ShouldWrapTheValueInSingleQuotes()
    {
        "a comment".ToSqlLiteral().Should().Be("'a comment'");
    }

    /// <summary>
    /// Comments are written straight into the script rather than bound, so an apostrophe would end
    /// the literal early and leave the rest of the comment to be read as SQL.
    /// </summary>
    [Fact]
    public void ToSqlLiteral_WhenTheValueHoldsAnApostrophe_ShouldDoubleIt()
    {
        "don't truncate".ToSqlLiteral().Should().Be("'don''t truncate'");
    }

    [Fact]
    public void ToSqlLiteral_WhenTheValueIsEmpty_ShouldStillBeALiteral()
    {
        string.Empty.ToSqlLiteral().Should().Be("''");
    }
}
