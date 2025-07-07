using FluentAssertions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Xunit;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle.Tests;

public class OracleObjectsGraphTests
{
    [Fact]
    public void GetGraph_WhenObjectsHaveDependencies_ShouldReturnCorrectOrder()
    {
        // Arrange
        var table1 = new DbObject("TABLE1", "TABLE");
        var table2 = new DbObject("TABLE2", "TABLE");
        var view1 = new DbObject("VIEW1", "VIEW");
        var function1 = new DbObject("FUNCTION1", "FUNCTION");

        var dbObjects = new List<DbObject> { table1, table2, view1, function1 };
        var dependencies = new List<OracleObjectDependencies>
        {
            new(view1, table1),
            new(view1, table2),
            new(function1, view1)
        };

        var graph = new OracleObjectsGraph(dbObjects, dependencies);

        // Act
        var result = graph.GetGraph();

        // Assert
        result.Should().Equal(table1, table2, view1, function1);
    }

    [Fact]
    public void GetGraph_WhenCircularDependencyDetected_ShouldThrowException()
    {
        // Arrange
        var view1 = new DbObject("VIEW1", "VIEW");
        var view2 = new DbObject("VIEW2", "VIEW");

        var dbObjects = new List<DbObject> { view1, view2 };
        var dependencies = new List<OracleObjectDependencies>
        {
            new(view1, view2),
            new(view2, view1)
        };

        var graph = new OracleObjectsGraph(dbObjects, dependencies);

        // Act
        Action act = () => graph.GetGraph();

        // Assert
        act.Should().Throw<Exception>().WithMessage("Circular dependency detected: VIEW1,VIEW2");
    }

    [Fact]
    public void GetGraph_WhenObjectsHaveMultipleDependencies_ShouldReturnCorrectOrder()
    {
        // Arrange
        var table1 = new DbObject("TABLE1", "TABLE");
        var table2 = new DbObject("TABLE2", "TABLE");
        var view1 = new DbObject("VIEW1", "VIEW");
        var view2 = new DbObject("VIEW2", "VIEW");
        var function1 = new DbObject("FUNCTION1", "FUNCTION");

        var dbObjects = new List<DbObject> { table1, table2, view1, view2, function1 };
        var dependencies = new List<OracleObjectDependencies>
        {
            new(view1, table1),
            new(view1, table2),
            new(view2, table1),
            new(function1, view1),
            new(function1, view2)
        };

        var graph = new OracleObjectsGraph(dbObjects, dependencies);

        // Act
        var result = graph.GetGraph();

        // Assert
        result.Should().Equal(table1, table2, view1, view2, function1);
    }
}
