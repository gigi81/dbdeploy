using Grillisoft.Tools.DatabaseDeploy.SqlServer.Ddl;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer.Tests.Ddl;

public class SqlServerObjectTypeTests
{
    [Test]
    [Arguments("U", SqlServerObjectType.Table)]
    [Arguments("V", SqlServerObjectType.View)]
    [Arguments("P", SqlServerObjectType.Procedure)]
    [Arguments("FN", SqlServerObjectType.Function)]
    [Arguments("IF", SqlServerObjectType.Function)]
    [Arguments("TF", SqlServerObjectType.Function)]
    [Arguments("SN", SqlServerObjectType.Synonym)]
    [Arguments("SO", SqlServerObjectType.Sequence)]
    public void FromSysType_ShouldMapTheCatalogCode(string sysType, string expected)
    {
        SqlServerObjectType.FromSysType(sysType)!.Name.Should().Be(expected);
    }

    /// <summary>
    /// <c>sys.objects.type</c> is a char(2), so every code but the two letter ones comes back padded.
    /// </summary>
    [Test]
    public void FromSysType_ShouldIgnoreTheCatalogPadding()
    {
        SqlServerObjectType.FromSysType("U ")!.Name.Should().Be(SqlServerObjectType.Table);
    }

    /// <summary>
    /// A CLR module cannot be created without its assembly, and an assembly is not something a text
    /// script can carry, so those are reported as unsupported rather than scripted badly.
    /// </summary>
    [Test]
    [Arguments("PC")]
    [Arguments("FS")]
    [Arguments("FT")]
    [Arguments("TA")]
    [Arguments("SQ")]
    public void FromSysType_WhenTheTypeCannotBeScripted_ShouldReturnNull(string sysType)
    {
        SqlServerObjectType.FromSysType(sysType).Should().BeNull();
    }

    [Test]
    public void RankOf_ShouldPutEverythingAfterWhatItIsBuiltOn()
    {
        SqlServerObjectType.RankOf(SqlServerObjectType.Schema)
            .Should().BeLessThan(SqlServerObjectType.RankOf(SqlServerObjectType.Table));

        SqlServerObjectType.RankOf(SqlServerObjectType.Type)
            .Should().BeLessThan(SqlServerObjectType.RankOf(SqlServerObjectType.Table));

        SqlServerObjectType.RankOf(SqlServerObjectType.Table)
            .Should().BeLessThan(SqlServerObjectType.RankOf(SqlServerObjectType.View));

        SqlServerObjectType.RankOf(SqlServerObjectType.View)
            .Should().BeLessThan(SqlServerObjectType.RankOf(SqlServerObjectType.Index));

        SqlServerObjectType.RankOf(SqlServerObjectType.Index)
            .Should().BeLessThan(SqlServerObjectType.RankOf(SqlServerObjectType.ForeignKey));

        SqlServerObjectType.RankOf(SqlServerObjectType.ForeignKey)
            .Should().BeLessThan(SqlServerObjectType.RankOf(SqlServerObjectType.Trigger));
    }

    /// <summary>An unknown type must never push a known one down the script.</summary>
    [Test]
    public void RankOf_WhenTheTypeIsUnknown_ShouldSortLast()
    {
        SqlServerObjectType.RankOf("SOMETHING NEW").Should().Be(int.MaxValue);
    }

    [Test]
    public void All_ShouldHaveDistinctNamesAndRanks()
    {
        SqlServerObjectType.All.Select(t => t.Name).Should().OnlyHaveUniqueItems();
        SqlServerObjectType.All.Select(t => t.Rank).Should().OnlyHaveUniqueItems();
    }
}
