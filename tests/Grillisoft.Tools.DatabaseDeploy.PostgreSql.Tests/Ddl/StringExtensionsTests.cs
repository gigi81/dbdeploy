using Grillisoft.Tools.DatabaseDeploy.PostgreSql.Ddl;

namespace Grillisoft.Tools.DatabaseDeploy.PostgreSql.Tests.Ddl;

public class StringExtensionsTests
{
    /// <summary>
    /// Every identifier is quoted, not only the ones that need it: PostgreSQL folds an unquoted
    /// name to lower case, so a table called Orders only comes back as itself when it is quoted.
    /// </summary>
    [Test]
    public void Quote_ShouldWrapEveryNameInDoubleQuotes()
    {
        "Orders".Quote().Should().Be("\"Orders\"");
    }

    [Test]
    public void Quote_ShouldDoubleAQuoteInsideTheName()
    {
        "we\"ird".Quote().Should().Be("\"we\"\"ird\"");
    }

    [Test]
    public void Qualify_ShouldPrefixTheSchema()
    {
        "customer".Qualify("app").Should().Be("\"app\".\"customer\"");
    }

    [Test]
    public void Qualify_WhenThereIsNoSchema_ShouldJustQuote()
    {
        "customer".Qualify(null).Should().Be("\"customer\"");
    }

    [Test]
    public void ToSqlLiteral_ShouldDoubleTheQuotes()
    {
        "it's".ToSqlLiteral().Should().Be("'it''s'");
    }

    /// <summary>
    /// The migrations table arrives already schema prefixed, and can have been configured
    /// qualified, quoted, or both; pg_catalog holds the two halves separately and holds neither.
    /// </summary>
    [Test]
    [Arguments("public.__Migrations", "__Migrations", "public")]
    [Arguments("app.__Migrations", "__Migrations", "app")]
    [Arguments("\"my schema\".\"__Migrations\"", "__Migrations", "my schema")]
    [Arguments("__Migrations", "__Migrations", null)]
    public void UnqualifiedAndSchemaOf_ShouldSplitTheConfiguredName(
        string configured,
        string expectedName,
        string? expectedSchema)
    {
        configured.Unqualified().Should().Be(expectedName);
        configured.SchemaOf().Should().Be(expectedSchema);
    }
}
