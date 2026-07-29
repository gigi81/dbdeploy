using AwesomeAssertions;
using Grillisoft.Tools.DatabaseDeploy.Oracle.Ddl;
using Xunit;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle.Tests.Ddl;

public class OracleObjectTypeTests
{
    /// <summary>
    /// DBMS_METADATA spells a composite object type with an underscore and gives the specification
    /// of a package or a type its own name. Handing it the ALL_OBJECTS spelling raises ORA-31600 and
    /// no package body, type body or materialized view ever makes it into the script.
    /// </summary>
    [Theory]
    [InlineData("PACKAGE", "PACKAGE_SPEC")]
    [InlineData("PACKAGE BODY", "PACKAGE_BODY")]
    [InlineData("TYPE", "TYPE_SPEC")]
    [InlineData("TYPE BODY", "TYPE_BODY")]
    [InlineData("MATERIALIZED VIEW", "MATERIALIZED_VIEW")]
    [InlineData("TABLE", "TABLE")]
    [InlineData("VIEW", "VIEW")]
    public void Find_ShouldMapToTheDbmsMetadataObjectType(string objectType, string expected)
    {
        OracleObjectType.Find(objectType)!.MetadataType.Should().Be(expected);
    }

    [Fact]
    public void MetadataType_ShouldNeverHoldASpace()
    {
        OracleObjectType.All.Should().OnlyContain(t => !t.MetadataType.Contains(' '));
    }

    [Fact]
    public void Find_WhenTypeIsNotSupported_ShouldReturnNull()
    {
        OracleObjectType.Find("JAVA CLASS").Should().BeNull();
        OracleObjectType.RankOf("JAVA CLASS").Should().Be(int.MaxValue);
    }

    /// <summary>
    /// Foreign keys are synthesized from ALL_CONSTRAINTS, so asking ALL_OBJECTS for them would
    /// return nothing and quietly drop every constraint.
    /// </summary>
    [Fact]
    public void QueryableNames_ShouldNotHoldTheForeignKeyPseudoType()
    {
        OracleObjectType.QueryableNames.Should().NotContain(OracleObjectType.RefConstraint);
        OracleObjectType.All.Should().Contain(t => t.Name == OracleObjectType.RefConstraint);
    }

    /// <summary>
    /// Anything that can only exist on top of a table has to rank after it, so that the ordering
    /// falls back to something deployable when the server declares no dependency at all.
    /// </summary>
    [Fact]
    public void Rank_ShouldPutTablesBeforeEverythingBuiltOnThem()
    {
        var table = OracleObjectType.RankOf("TABLE");

        foreach (var type in new[] { "VIEW", "INDEX", OracleObjectType.RefConstraint, "TRIGGER", "PACKAGE BODY" })
            OracleObjectType.RankOf(type).Should().BeGreaterThan(table, $"{type} needs its table first");

        OracleObjectType.RankOf("TYPE").Should().BeLessThan(table);
        OracleObjectType.RankOf("PACKAGE BODY").Should().BeGreaterThan(OracleObjectType.RankOf("PACKAGE"));
        OracleObjectType.RankOf("TYPE BODY").Should().BeGreaterThan(OracleObjectType.RankOf("TYPE"));
    }
}
