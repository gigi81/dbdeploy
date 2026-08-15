using Grillisoft.Tools.DatabaseDeploy.PostgreSql.Ddl;

namespace Grillisoft.Tools.DatabaseDeploy.PostgreSql.Tests.Ddl;

public class PostgreSqlObjectTypeTests
{
    /// <summary>
    /// The order the script falls back on when the dependencies leave it open: schemas, then the
    /// types and sequences everything else is built out of, then the tables, then what is built on
    /// top of them, and last everything that can only exist once its table does.
    /// </summary>
    [Test]
    public void RankOf_ShouldOrderTheTypesTheWayAScriptHasToBeWritten()
    {
        var ranks = new[]
        {
            PostgreSqlObjectType.RankOf(PostgreSqlObjectType.Schema),
            PostgreSqlObjectType.RankOf(PostgreSqlObjectType.Type),
            PostgreSqlObjectType.RankOf(PostgreSqlObjectType.Domain),
            PostgreSqlObjectType.RankOf(PostgreSqlObjectType.Sequence),
            PostgreSqlObjectType.RankOf(PostgreSqlObjectType.Table),
            PostgreSqlObjectType.RankOf(PostgreSqlObjectType.Partition),
            PostgreSqlObjectType.RankOf(PostgreSqlObjectType.View),
            PostgreSqlObjectType.RankOf(PostgreSqlObjectType.MaterializedView),
            PostgreSqlObjectType.RankOf(PostgreSqlObjectType.Constraint),
            PostgreSqlObjectType.RankOf(PostgreSqlObjectType.Index),
            PostgreSqlObjectType.RankOf(PostgreSqlObjectType.ForeignKey),
            PostgreSqlObjectType.RankOf(PostgreSqlObjectType.Trigger),
        };

        ranks.Should().BeInAscendingOrder();
    }

    /// <summary>
    /// A partition has to be attached after both its parent and itself exist, and a sequence can
    /// only be owned once its table does.
    /// </summary>
    [Test]
    public void RankOf_ShouldOrderTheTyingStatementsAfterTheTablesTheyTie()
    {
        PostgreSqlObjectType.RankOf(PostgreSqlObjectType.Table)
            .Should().BeLessThan(PostgreSqlObjectType.RankOf(PostgreSqlObjectType.Partition));

        PostgreSqlObjectType.RankOf(PostgreSqlObjectType.Sequence)
            .Should().BeLessThan(PostgreSqlObjectType.RankOf(PostgreSqlObjectType.SequenceOwner));
    }

    [Test]
    public void RankOf_WhenTheTypeIsUnknown_ShouldSortLast()
    {
        PostgreSqlObjectType.RankOf("FOREIGN TABLE").Should().Be(int.MaxValue);
    }

    [Test]
    [Arguments('r', PostgreSqlObjectType.Table)]
    [Arguments('p', PostgreSqlObjectType.Table)]
    [Arguments('v', PostgreSqlObjectType.View)]
    [Arguments('m', PostgreSqlObjectType.MaterializedView)]
    [Arguments('S', PostgreSqlObjectType.Sequence)]
    [Arguments('i', PostgreSqlObjectType.Index)]
    [Arguments('I', PostgreSqlObjectType.Index)]
    public void FromRelKind_ShouldMapTheCatalogsSpelling(char relKind, string expected)
    {
        PostgreSqlObjectType.FromRelKind(relKind).Should().Be(expected);
    }

    /// <summary>A foreign table is a relation this tool does not script, and must not guess at.</summary>
    [Test]
    public void FromRelKind_WhenTheKindIsNotScripted_ShouldReturnNull()
    {
        PostgreSqlObjectType.FromRelKind('f').Should().BeNull();
    }

    [Test]
    [Arguments('e', PostgreSqlObjectType.Type)]
    [Arguments('c', PostgreSqlObjectType.Type)]
    [Arguments('r', PostgreSqlObjectType.Type)]
    [Arguments('d', PostgreSqlObjectType.Domain)]
    public void FromTypType_ShouldMapTheCatalogsSpelling(char typType, string expected)
    {
        PostgreSqlObjectType.FromTypType(typType).Should().Be(expected);
    }

    /// <summary>
    /// An aggregate has its own type because pg_get_functiondef raises an error on one rather than
    /// returning anything: it has to be assembled by hand, so it must not be mistaken for a
    /// function.
    /// </summary>
    [Test]
    [Arguments('f', PostgreSqlObjectType.Function)]
    [Arguments('w', PostgreSqlObjectType.Function)]
    [Arguments('p', PostgreSqlObjectType.Procedure)]
    [Arguments('a', PostgreSqlObjectType.Aggregate)]
    public void FromProKind_ShouldMapTheCatalogsSpelling(char proKind, string expected)
    {
        PostgreSqlObjectType.FromProKind(proKind).Should().Be(expected);
    }
}
