using System.Data.Common;

namespace Grillisoft.Tools.DatabaseDeploy.Database.Tests;

/// <summary>
/// The contract worth pinning here is the difference between the two methods: a query the
/// generation cannot continue without, and a query whose failure only makes the script poorer.
/// </summary>
public class CatalogReaderTests
{
    private const string AnySql = "SELECT name FROM sys.objects";

    private static CatalogReader Reader(DbCommand command, RecordingLogger logger)
        => new(_ => command, "HR", logger);

    [Test]
    public async Task Query_ShouldReturnOneItemPerRow()
    {
        // Arrange
        var command = FakeDbCommand.Returning("CUSTOMER", "ORDERS");
        var reader = Reader(command, new RecordingLogger());

        // Act
        var result = await reader.Query(AnySql, "tables", r => r.GetString(0), CancellationToken.None);

        // Assert
        result.Should().Equal("CUSTOMER", "ORDERS");
    }

    [Test]
    public async Task Query_ShouldBindTheParametersItWasGiven()
    {
        // Arrange
        var command = FakeDbCommand.Returning();
        var reader = Reader(command, new RecordingLogger());

        // Act
        await reader.Query(AnySql, "tables", r => r.GetString(0), CancellationToken.None,
            ("owner", "HR"), ("migration_table", "MIGRATIONS"));

        // Assert
        command.BoundParameters.Should().Equal(("owner", (object?)"HR"), ("migration_table", (object?)"MIGRATIONS"));
    }

    /// <summary>
    /// Without the object list there is nothing to script, so this failure has to be the caller's
    /// problem.
    /// </summary>
    [Test]
    public async Task Query_WhenTheServerRefuses_ShouldThrow()
    {
        // Arrange
        var command = FakeDbCommand.Throwing(new InvalidOperationException("no privileges"));
        var reader = Reader(command, new RecordingLogger());

        // Act
        var act = () => reader.Query(AnySql, "tables", r => r.GetString(0), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("no privileges");
    }

    /// <summary>
    /// A login that is not granted on one of the optional views should cost a warning and a slightly
    /// poorer script, not the whole generation.
    /// </summary>
    [Test]
    public async Task TryQuery_WhenTheServerRefuses_ShouldWarnAndReturnNothing()
    {
        // Arrange
        var logger = new RecordingLogger();
        var command = FakeDbCommand.Throwing(new InvalidOperationException("no privileges"));
        var reader = Reader(command, logger);

        // Act
        var result = await reader.TryQuery(AnySql, "constraint indexes", r => r.GetString(0), CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
        logger.Warnings.Should().ContainSingle()
              .Which.Should().Contain("constraint indexes").And.Contain("HR");
    }

    [Test]
    public async Task TryQuery_WhenTheServerAnswers_ShouldReturnTheRows()
    {
        // Arrange
        var logger = new RecordingLogger();
        var reader = Reader(FakeDbCommand.Returning("IX_ONE"), logger);

        // Act
        var result = await reader.TryQuery(AnySql, "indexes", r => r.GetString(0), CancellationToken.None);

        // Assert
        result.Should().Equal("IX_ONE");
        logger.Warnings.Should().BeEmpty();
    }

    /// <summary>
    /// Oracle folds unquoted names to upper case and compares them ordinally; SQL Server does not
    /// care about case. The caller says which, and the set has to honour it.
    /// </summary>
    [Test]
    public async Task TryQueryNames_ShouldCompareNamesTheWayTheCallerAsked()
    {
        // Arrange
        var reader = Reader(FakeDbCommand.Returning("Orders", "ORDERS"), new RecordingLogger());
        var caseSensitive = Reader(FakeDbCommand.Returning("Orders", "ORDERS"), new RecordingLogger());

        // Act
        var ignoringCase = await reader.TryQueryNames(
            AnySql, "tables", StringComparer.OrdinalIgnoreCase, CancellationToken.None);

        var ordinal = await caseSensitive.TryQueryNames(
            AnySql, "tables", StringComparer.Ordinal, CancellationToken.None);

        // Assert
        ignoringCase.Should().HaveCount(1);
        ordinal.Should().HaveCount(2);
    }

    [Test]
    public async Task TryQueryNames_WhenTheServerRefuses_ShouldReturnAnEmptySet()
    {
        // Arrange
        var reader = Reader(FakeDbCommand.Throwing(new InvalidOperationException("nope")), new RecordingLogger());

        // Act
        var names = await reader.TryQueryNames(AnySql, "tables", StringComparer.Ordinal, CancellationToken.None);

        // Assert
        names.Should().BeEmpty();
    }
}
