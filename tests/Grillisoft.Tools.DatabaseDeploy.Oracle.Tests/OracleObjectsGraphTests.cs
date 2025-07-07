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
}
