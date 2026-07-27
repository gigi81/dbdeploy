using AwesomeAssertions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Database;
using Xunit;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer.Tests;

/// <summary>
/// The ordering the SQL Server generator asks for. The sort itself is
/// <see cref="DbObjectsGraph"/>, shared with the other providers; what is checked here is that the
/// ranks of <see cref="SqlServerObjectType"/> put a real database in a replayable order.
/// </summary>
public class SqlServerObjectsGraphTests
{
    private static DbObjectsGraph Graph(
        IEnumerable<DbObject> objects,
        params (DbObject DbObject, DbObject DependsOn)[] dependencies)
        => new(objects, dependencies, SqlServerObjectType.RankOf);

    [Fact]
    public void GetGraph_ShouldCreateTheSchemaBeforeAnythingInIt()
    {
        var schema = new DbObject("[app]", SqlServerObjectType.Schema);
        var table = new DbObject("[app].[Orders]", SqlServerObjectType.Table);
        var procedure = new DbObject("[app].[pr_AddOrder]", SqlServerObjectType.Procedure);

        var result = Graph(
            [procedure, table, schema],
            (table, schema),
            (procedure, schema),
            (procedure, table)).GetGraph();

        result.Should().Equal(schema, table, procedure);
    }

    /// <summary>
    /// The whole point of scripting foreign keys on their own: two tables pointing at each other is
    /// ordinary in SQL Server, and neither can be created with the constraint inline.
    /// </summary>
    [Fact]
    public void GetGraph_WhenTwoTablesPointAtEachOther_ShouldWriteBothKeysLast()
    {
        var customer = new DbObject("[app].[Customer]", SqlServerObjectType.Table);
        var orders = new DbObject("[app].[Orders]", SqlServerObjectType.Table);
        var toCustomer = new DbObject("[app].[FK_Orders_Customer]", SqlServerObjectType.ForeignKey);
        var toOrder = new DbObject("[app].[FK_Customer_LastOrder]", SqlServerObjectType.ForeignKey);

        var graph = Graph(
            [toOrder, toCustomer, orders, customer],
            (toCustomer, orders),
            (toCustomer, customer),
            (toOrder, customer),
            (toOrder, orders));

        var result = graph.GetGraph();

        result.Should().Equal(customer, orders, toOrder, toCustomer);
        graph.BrokenCycles.Should().BeEmpty("scripting the keys separately is what makes the cycle disappear");
    }

    /// <summary>
    /// A table can only be created once the type of one of its columns exists, and the index on it
    /// only once the table does.
    /// </summary>
    [Fact]
    public void GetGraph_ShouldOrderTypeThenTableThenIndex()
    {
        var type = new DbObject("[app].[Code]", SqlServerObjectType.Type);
        var table = new DbObject("[app].[Customer]", SqlServerObjectType.Table);
        var index = new DbObject("[app].[Customer].[IX_Code]", SqlServerObjectType.Index);

        var result = Graph([index, table, type], (table, type), (index, table)).GetGraph();

        result.Should().Equal(type, table, index);
    }

    /// <summary>
    /// A view calling a function has to come after it, even though a view outranks a function.
    /// </summary>
    [Fact]
    public void GetGraph_WhenAViewCallsAFunction_ShouldWriteTheFunctionFirst()
    {
        var function = new DbObject("[app].[fn_Tax]", SqlServerObjectType.Function);
        var view = new DbObject("[app].[v_Totals]", SqlServerObjectType.View);

        var result = Graph([view, function], (view, function)).GetGraph();

        result.Should().Equal(function, view);
    }

    /// <summary>
    /// An indexed view carries an index of its own, which cannot be created before the view.
    /// </summary>
    [Fact]
    public void GetGraph_ShouldWriteAnIndexOnAViewAfterTheView()
    {
        var table = new DbObject("[app].[Orders]", SqlServerObjectType.Table);
        var view = new DbObject("[app].[v_PerCustomer]", SqlServerObjectType.View);
        var index = new DbObject("[app].[v_PerCustomer].[UX_PerCustomer]", SqlServerObjectType.Index);

        var result = Graph([index, view, table], (view, table), (index, view)).GetGraph();

        result.Should().Equal(table, view, index);
    }

    /// <summary>
    /// Two procedures calling each other is legal, and must not stop the generation.
    /// </summary>
    [Fact]
    public void GetGraph_WhenTwoProceduresCallEachOther_ShouldStillReturnBoth()
    {
        var first = new DbObject("[dbo].[pr_First]", SqlServerObjectType.Procedure);
        var second = new DbObject("[dbo].[pr_Second]", SqlServerObjectType.Procedure);

        var graph = Graph([first, second], (first, second), (second, first));
        var result = graph.GetGraph();

        result.Should().BeEquivalentTo(new[] { first, second });
        graph.BrokenCycles.Should().ContainSingle();
    }

    /// <summary>
    /// A dependency on something that is not being scripted - a CLR module, an object in another
    /// database, an object dropped between two queries - is dropped, not fatal.
    /// </summary>
    [Fact]
    public void GetGraph_WhenADependencyIsNotScripted_ShouldIgnoreIt()
    {
        var table = new DbObject("[dbo].[Orders]", SqlServerObjectType.Table);
        var view = new DbObject("[dbo].[v_Orders]", SqlServerObjectType.View);
        var clr = new DbObject("[dbo].[clr_Something]", "CLR PROCEDURE");

        var graph = Graph([table, view], (view, table), (view, clr), (clr, table));
        var result = graph.GetGraph();

        result.Should().Equal(table, view);
        graph.IgnoredDependencies.Should().Be(2);
    }
}
