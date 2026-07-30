using Grillisoft.Tools.DatabaseDeploy.SqlServer.Ddl;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer.Tests.Ddl;

public class SqlServerObjectTests
{
    private static SqlServerObject Table(string schema, string name)
        => new(SqlServerObjectType.Find(SqlServerObjectType.Table)!, schema, name);

    private static SqlServerObject Index(string schema, string table, string name)
        => new(SqlServerObjectType.Find(SqlServerObjectType.Index)!, schema, name, schema, table);

    [Test]
    public void QualifiedName_ShouldQuoteAndSchemaQualifyTheName()
    {
        Table("app", "Orders").QualifiedName.Should().Be("[app].[Orders]");
    }

    /// <summary>
    /// Schemas, partition functions and partition schemes are not schema scoped, so there is nothing
    /// to qualify them with.
    /// </summary>
    [Test]
    public void QualifiedName_WhenTheObjectIsNotSchemaScoped_ShouldBeTheNameAlone()
    {
        var schema = new SqlServerObject(SqlServerObjectType.Find(SqlServerObjectType.Schema)!, string.Empty, "app");

        schema.QualifiedName.Should().Be("[app]");
    }

    /// <summary>
    /// An index name is only unique within its table, so two tables carrying an index of the same
    /// name have to stay two objects.
    /// </summary>
    [Test]
    public void QualifiedName_WhenTheObjectHangsOffATable_ShouldIncludeIt()
    {
        Index("app", "Orders", "IX_Created").QualifiedName.Should().Be("[app].[Orders].[IX_Created]");
    }

    [Test]
    public void Key_ShouldDistinguishTwoIndexesOfTheSameNameOnDifferentTables()
    {
        Index("app", "Orders", "IX_Created").Key
            .Should().NotBe(Index("app", "Customer", "IX_Created").Key);
    }

    /// <summary>
    /// A table and a view of the same name cannot both exist, but a table and its trigger can share
    /// one, so the type has to be part of the identity.
    /// </summary>
    [Test]
    public void Key_ShouldIncludeTheType()
    {
        Table("app", "Orders").Key.Should().Be("[app].[Orders]---TABLE");
    }

    [Test]
    public void DbObject_ShouldCarryTheTypeNameTheGraphOrdersBy()
    {
        Table("app", "Orders").DbObject.Type.Should().Be(SqlServerObjectType.Table);
    }

    [Test]
    public void ParentName_ShouldBeNullForAnObjectThatDoesNotHangOffAnother()
    {
        var table = Table("app", "Orders");

        table.ParentName.Should().BeNull();
        table.ParentSchema.Should().BeNull();
    }

    [Test]
    public void ToString_ShouldReadAsTheTypeAndTheName()
    {
        Table("app", "Orders").ToString().Should().Be("TABLE [app].[Orders]");
    }
}
