using Grillisoft.Tools.DatabaseDeploy.MySql.Ddl;

namespace Grillisoft.Tools.DatabaseDeploy.MySql.Tests.Ddl;

public class MySqlObjectTypeTests
{
    [Test]
    public void Find_ShouldIgnoreCase()
    {
        MySqlObjectType.Find("function").Should().BeSameAs(MySqlObjectType.Find("FUNCTION"));
    }

    [Test]
    public void Find_WhenTheTypeIsNotSupported_ShouldReturnNull()
    {
        MySqlObjectType.Find("SYSTEM VIEW").Should().BeNull();
    }

    /// <summary>
    /// A type nothing knows about must not push a known one down the script, so it sorts last.
    /// </summary>
    [Test]
    public void RankOf_WhenTheTypeIsUnknown_ShouldSortLast()
    {
        MySqlObjectType.RankOf("SYSTEM VIEW").Should().Be(int.MaxValue);
    }

    /// <summary>
    /// The order the script depends on when the dependencies leave it open: tables before the
    /// program units, views after the functions they call, and the things that need a table last.
    /// </summary>
    [Test]
    public void RankOf_ShouldOrderTablesBeforeViewsBeforeKeysAndTriggers()
    {
        var ranks = new[]
        {
            MySqlObjectType.RankOf(MySqlObjectType.Sequence),
            MySqlObjectType.RankOf(MySqlObjectType.Table),
            MySqlObjectType.RankOf(MySqlObjectType.Function),
            MySqlObjectType.RankOf(MySqlObjectType.View),
            MySqlObjectType.RankOf(MySqlObjectType.ForeignKey),
            MySqlObjectType.RankOf(MySqlObjectType.Trigger),
        };

        ranks.Should().BeInAscendingOrder();
    }

    /// <summary>
    /// A package body can only be written after its specification, whatever the dependencies say.
    /// </summary>
    [Test]
    public void RankOf_ShouldOrderAPackageBodyAfterItsSpecification()
    {
        MySqlObjectType.RankOf(MySqlObjectType.Package)
            .Should().BeLessThan(MySqlObjectType.RankOf(MySqlObjectType.PackageBody));
    }

    /// <summary>
    /// The scripter reads the DDL out of the result by column name, so a type whose name is wrong
    /// silently scripts nothing. Every type that has a statement has to name its column.
    /// </summary>
    [Test]
    public void All_ShouldNameTheColumnHoldingTheDdl()
    {
        MySqlObjectType.All
            .Where(type => type.ShowStatement.Length > 0)
            .Should().AllSatisfy(type => type.DdlColumn.Should().NotBeEmpty());
    }

    /// <summary>
    /// The one type that is not read from the server: a foreign key is taken out of its table's
    /// CREATE TABLE, so it has no statement of its own.
    /// </summary>
    [Test]
    public void All_OnlyTheForeignKeyShouldHaveNoStatement()
    {
        MySqlObjectType.All.Where(type => type.ShowStatement.Length == 0)
            .Select(type => type.Name)
            .Should().Equal(MySqlObjectType.ForeignKey);
    }
}
