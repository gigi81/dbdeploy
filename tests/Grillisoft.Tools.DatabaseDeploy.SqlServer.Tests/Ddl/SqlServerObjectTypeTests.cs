using AwesomeAssertions;
using Grillisoft.Tools.DatabaseDeploy.SqlServer.Ddl;
using Xunit;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer.Tests.Ddl;

public class SqlServerObjectTypeTests
{
    [Theory]
    [InlineData("U", SqlServerObjectType.Table)]
    [InlineData("V", SqlServerObjectType.View)]
    [InlineData("P", SqlServerObjectType.Procedure)]
    [InlineData("FN", SqlServerObjectType.Function)]
    [InlineData("IF", SqlServerObjectType.Function)]
    [InlineData("TF", SqlServerObjectType.Function)]
    [InlineData("SN", SqlServerObjectType.Synonym)]
    [InlineData("SO", SqlServerObjectType.Sequence)]
    public void FromSysType_ShouldMapTheCatalogCode(string sysType, string expected)
    {
        SqlServerObjectType.FromSysType(sysType)!.Name.Should().Be(expected);
    }

    /// <summary>
    /// <c>sys.objects.type</c> is a char(2), so every code but the two letter ones comes back padded.
    /// </summary>
    [Fact]
    public void FromSysType_ShouldIgnoreTheCatalogPadding()
    {
        SqlServerObjectType.FromSysType("U ")!.Name.Should().Be(SqlServerObjectType.Table);
    }

    /// <summary>
    /// A CLR module cannot be created without its assembly, and an assembly is not something a text
    /// script can carry, so those are reported as unsupported rather than scripted badly.
    /// </summary>
    [Theory]
    [InlineData("PC")]
    [InlineData("FS")]
    [InlineData("FT")]
    [InlineData("TA")]
    [InlineData("SQ")]
    public void FromSysType_WhenTheTypeCannotBeScripted_ShouldReturnNull(string sysType)
    {
        SqlServerObjectType.FromSysType(sysType).Should().BeNull();
    }

    [Fact]
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
    [Fact]
    public void RankOf_WhenTheTypeIsUnknown_ShouldSortLast()
    {
        SqlServerObjectType.RankOf("SOMETHING NEW").Should().Be(int.MaxValue);
    }

    [Fact]
    public void All_ShouldHaveDistinctNamesAndRanks()
    {
        SqlServerObjectType.All.Select(t => t.Name).Should().OnlyHaveUniqueItems();
        SqlServerObjectType.All.Select(t => t.Rank).Should().OnlyHaveUniqueItems();
    }
}
