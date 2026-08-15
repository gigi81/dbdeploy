using Grillisoft.Tools.DatabaseDeploy.MySql.Ddl;

namespace Grillisoft.Tools.DatabaseDeploy.MySql.Tests.Ddl;

public class StringExtensionsTests
{
    [Test]
    public void Quote_ShouldWrapTheNameInBackticks()
    {
        "orders".Quote().Should().Be("`orders`");
    }

    /// <summary>
    /// Object names reach the server inside the text of a <c>SHOW CREATE</c>, which takes no
    /// parameters, so a name holding a backtick has to be escaped rather than trusted.
    /// </summary>
    [Test]
    public void Quote_ShouldDoubleABacktickInsideTheName()
    {
        "we`ird".Quote().Should().Be("`we``ird`");
    }

    [Test]
    [Arguments("__Migrations", "__Migrations")]
    [Arguments("mydb.__Migrations", "__Migrations")]
    [Arguments("`mydb`.`__Migrations`", "__Migrations")]
    [Arguments("`__Migrations`", "__Migrations")]
    public void Unqualified_ShouldDropTheDatabasePrefixAndTheBackticks(string configured, string expected)
    {
        configured.Unqualified().Should().Be(expected);
    }
}
