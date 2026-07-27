using AwesomeAssertions;
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

    /// <summary>
    /// Mutually recursive package bodies are legal in Oracle, so a cycle must not stop the
    /// generation: the objects are still written and the cycle is reported.
    /// </summary>
    [Fact]
    public void GetGraph_WhenCircularDependencyDetected_ShouldStillReturnAllObjects()
    {
        // Arrange
        var body1 = new DbObject("PKG1", "PACKAGE BODY");
        var body2 = new DbObject("PKG2", "PACKAGE BODY");

        var dbObjects = new List<DbObject> { body1, body2 };
        var dependencies = new List<OracleObjectDependencies>
        {
            new(body1, body2),
            new(body2, body1)
        };

        var graph = new OracleObjectsGraph(dbObjects, dependencies);

        // Act
        var result = graph.GetGraph();

        // Assert
        result.Should().BeEquivalentTo(dbObjects);
        graph.BrokenCycles.Should().ContainSingle()
             .Which.Should().BeEquivalentTo(dbObjects);
    }

    /// <summary>
    /// A dependency cycle must not drag the objects sitting behind it out of the result.
    /// </summary>
    [Fact]
    public void GetGraph_WhenObjectDependsOnCycle_ShouldReturnItAfterTheCycle()
    {
        // Arrange
        var body1 = new DbObject("PKG1", "PACKAGE BODY");
        var body2 = new DbObject("PKG2", "PACKAGE BODY");
        var trigger = new DbObject("TRG1", "TRIGGER");

        var dbObjects = new List<DbObject> { trigger, body1, body2 };
        var dependencies = new List<OracleObjectDependencies>
        {
            new(body1, body2),
            new(body2, body1),
            new(trigger, body1)
        };

        var graph = new OracleObjectsGraph(dbObjects, dependencies);

        // Act
        var result = graph.GetGraph();

        // Assert
        result.Should().HaveCount(3);
        result[^1].Should().Be(trigger);
    }

    /// <summary>
    /// ALL_DEPENDENCIES happily points at objects that are not being scripted (unsupported types,
    /// filtered objects, objects dropped between two queries). Those must be dropped, not fatal.
    /// </summary>
    [Fact]
    public void GetGraph_WhenDependencyIsNotScripted_ShouldIgnoreIt()
    {
        // Arrange
        var table = new DbObject("TABLE1", "TABLE");
        var view = new DbObject("VIEW1", "VIEW");
        var unknown = new DbObject("SOME_JAVA_CLASS", "JAVA CLASS");

        var dbObjects = new List<DbObject> { table, view };
        var dependencies = new List<OracleObjectDependencies>
        {
            new(view, table),
            new(view, unknown),
            new(unknown, table)
        };

        var graph = new OracleObjectsGraph(dbObjects, dependencies);

        // Act
        var result = graph.GetGraph();

        // Assert
        result.Should().Equal(table, view);
        graph.IgnoredDependencies.Should().Be(2);
    }

    /// <summary>
    /// The whole point of the ordering: a foreign key can only be created once both tables exist,
    /// even when the table it points at is scripted late because of its own dependencies.
    /// </summary>
    [Fact]
    public void GetGraph_WhenTableDependsOnFunction_ShouldStillWriteForeignKeyLast()
    {
        // Arrange
        var function = new DbObject("FN_DEFAULT", "FUNCTION");
        var parent = new DbObject("PARENT", "TABLE");
        var child = new DbObject("CHILD", "TABLE");
        var foreignKey = new DbObject("FK_CHILD_PARENT", OracleObjectType.RefConstraint);

        var dbObjects = new List<DbObject> { foreignKey, child, parent, function };
        var dependencies = new List<OracleObjectDependencies>
        {
            // a virtual column or a check constraint calling a user function
            new(parent, function),
            new(foreignKey, child),
            new(foreignKey, parent)
        };

        var graph = new OracleObjectsGraph(dbObjects, dependencies);

        // Act
        var result = graph.GetGraph();

        // Assert
        result.Should().Equal(child, function, parent, foreignKey);
    }

    [Fact]
    public void GetGraph_ShouldWriteSpecificationBeforeBody()
    {
        // Arrange
        var spec = new DbObject("PKG1", "PACKAGE");
        var body = new DbObject("PKG1", "PACKAGE BODY");
        var table = new DbObject("TABLE1", "TABLE");

        var dbObjects = new List<DbObject> { body, spec, table };
        var dependencies = new List<OracleObjectDependencies>
        {
            new(body, spec),
            new(body, table)
        };

        var graph = new OracleObjectsGraph(dbObjects, dependencies);

        // Act
        var result = graph.GetGraph();

        // Assert
        result.Should().Equal(table, spec, body);
    }

    [Fact]
    public void GetGraph_WhenObjectDependsOnItself_ShouldNotDeadlock()
    {
        // Arrange
        var function = new DbObject("RECURSIVE_FN", "FUNCTION");

        var graph = new OracleObjectsGraph(
            [function],
            [new OracleObjectDependencies(function, function)]);

        // Act
        var result = graph.GetGraph();

        // Assert
        result.Should().Equal(function);
        graph.BrokenCycles.Should().BeEmpty();
    }
}
