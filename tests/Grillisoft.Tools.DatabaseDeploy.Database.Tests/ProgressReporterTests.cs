using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy.Database.Tests;

/// <summary>
/// The point of the reporter is that a long run says something without a run of a hundred objects
/// saying it a hundred times.
/// </summary>
public class ProgressReporterTests
{
    private static readonly DbObject Object = new("TABLE1", "TABLE");

    [Test]
    public void Advance_WhenThereAreFewObjects_ShouldOnlyReportTheLastOne()
    {
        // Arrange
        var logger = new RecordingLogger();
        var reporter = new ProgressReporter(3, logger);

        // Act
        for (var i = 0; i < 3; i++)
            reporter.Advance(Object);

        // Assert
        logger.Messages.Should().ContainSingle();
    }

    /// <summary>Twenty reports over the run, plus the last object landing on the interval.</summary>
    [Test]
    public void Advance_WhenThereAreManyObjects_ShouldReportAboutTwentyTimes()
    {
        // Arrange
        var logger = new RecordingLogger();
        var reporter = new ProgressReporter(2000, logger);

        // Act
        for (var i = 0; i < 2000; i++)
            reporter.Advance(Object);

        // Assert
        logger.Messages.Should().HaveCount(20);
    }

    /// <summary>
    /// Below the floor the interval would report every object, which is what the floor is for.
    /// </summary>
    [Test]
    public void Advance_ShouldNeverReportMoreOftenThanEveryTwentyFiveObjects()
    {
        // Arrange
        var logger = new RecordingLogger();
        var reporter = new ProgressReporter(100, logger);

        // Act
        for (var i = 0; i < 100; i++)
            reporter.Advance(Object);

        // Assert
        logger.Messages.Should().HaveCount(4);
    }

    [Test]
    public void Advance_ShouldReportHowFarAlongItIs()
    {
        // Arrange
        var logger = new RecordingLogger();
        var reporter = new ProgressReporter(50, logger);

        // Act
        for (var i = 0; i < 50; i++)
            reporter.Advance(Object);

        // Assert - the elapsed time is left out, it changes from one run to the next
        logger.Messages.Select(Untimed).Should().Equal(
            "Scripted 25/50 objects (50%)",
            "Scripted 50/50 objects (100%)");
    }

    private static string Untimed(string message) => message[..message.IndexOf(" in ", StringComparison.Ordinal)];
}
