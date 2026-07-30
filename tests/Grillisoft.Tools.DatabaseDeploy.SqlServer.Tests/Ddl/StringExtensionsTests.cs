using Grillisoft.Tools.DatabaseDeploy.SqlServer.Ddl;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer.Tests.Ddl;

public class StringExtensionsTests
{
    [Test]
    [Arguments("Orders", "[Orders]")]
    [Arguments("Order Details", "[Order Details]")]
    [Arguments("select", "[select]")]
    public void Quote_ShouldWrapTheNameInBrackets(string name, string expected)
    {
        name.Quote().Should().Be(expected);
    }

    /// <summary>
    /// A closing bracket is legal in a SQL Server identifier and has to be doubled, otherwise the
    /// quoting ends early and the rest of the name is read as syntax.
    /// </summary>
    [Test]
    public void Quote_WhenTheNameHoldsAClosingBracket_ShouldDoubleIt()
    {
        "we]rd".Quote().Should().Be("[we]]rd]");
    }

    [Test]
    public void Qualify_ShouldPrefixTheSchema()
    {
        "Orders".Qualify("app").Should().Be("[app].[Orders]");
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    public void Qualify_WhenThereIsNoSchema_ShouldQuoteTheNameOnly(string? schema)
    {
        "PF_ByYear".Qualify(schema).Should().Be("[PF_ByYear]");
    }

    /// <summary>
    /// The migrations table is whatever the configuration says it is; the catalog only ever holds
    /// the bare name.
    /// </summary>
    [Test]
    [Arguments("__Migrations", "__Migrations")]
    [Arguments("dbo.__Migrations", "__Migrations")]
    [Arguments("[__Migrations]", "__Migrations")]
    [Arguments("[dbo].[__Migrations]", "__Migrations")]
    public void Unqualified_ShouldStripTheSchemaAndTheBrackets(string name, string expected)
    {
        name.Unqualified().Should().Be(expected);
    }
}
