using AwesomeAssertions;
using Xunit;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer.Tests;

public class StringExtensionsTests
{
    [Theory]
    [InlineData("Orders", "[Orders]")]
    [InlineData("Order Details", "[Order Details]")]
    [InlineData("select", "[select]")]
    public void Quote_ShouldWrapTheNameInBrackets(string name, string expected)
    {
        name.Quote().Should().Be(expected);
    }

    /// <summary>
    /// A closing bracket is legal in a SQL Server identifier and has to be doubled, otherwise the
    /// quoting ends early and the rest of the name is read as syntax.
    /// </summary>
    [Fact]
    public void Quote_WhenTheNameHoldsAClosingBracket_ShouldDoubleIt()
    {
        "we]rd".Quote().Should().Be("[we]]rd]");
    }

    [Fact]
    public void Qualify_ShouldPrefixTheSchema()
    {
        "Orders".Qualify("app").Should().Be("[app].[Orders]");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Qualify_WhenThereIsNoSchema_ShouldQuoteTheNameOnly(string? schema)
    {
        "PF_ByYear".Qualify(schema).Should().Be("[PF_ByYear]");
    }

    /// <summary>
    /// The migrations table is whatever the configuration says it is; the catalog only ever holds
    /// the bare name.
    /// </summary>
    [Theory]
    [InlineData("__Migrations", "__Migrations")]
    [InlineData("dbo.__Migrations", "__Migrations")]
    [InlineData("[__Migrations]", "__Migrations")]
    [InlineData("[dbo].[__Migrations]", "__Migrations")]
    public void Unqualified_ShouldStripTheSchemaAndTheBrackets(string name, string expected)
    {
        name.Unqualified().Should().Be(expected);
    }
}
