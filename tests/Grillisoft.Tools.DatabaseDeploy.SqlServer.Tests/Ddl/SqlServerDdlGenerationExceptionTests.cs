using AwesomeAssertions;
using Grillisoft.Tools.DatabaseDeploy.SqlServer.Ddl;
using Xunit;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer.Tests.Ddl;

public class SqlServerDdlGenerationExceptionTests
{
    private static IEnumerable<(string Object, string Error)> Failures(int count)
        => Enumerable.Range(1, count).Select(i => ($"[dbo].[T{i}]---TABLE", $"Msg {i}: something"));

    [Fact]
    public void Message_ShouldNameTheDatabaseAndEveryFailure()
    {
        // Arrange
        var exception = new SqlServerDdlGenerationException("HR", Failures(2));

        // Act
        var message = exception.Message;

        // Assert
        message.Should().Contain("2 object(s) of database HR")
               .And.Contain("[dbo].[T1]---TABLE: Msg 1: something")
               .And.Contain("[dbo].[T2]---TABLE: Msg 2: something");
    }

    /// <summary>
    /// The script is written out in full before this is raised, so the message has to be clear that
    /// what is on disk is not deployable.
    /// </summary>
    [Fact]
    public void Message_ShouldSayTheScriptMustNotBeDeployed()
    {
        new SqlServerDdlGenerationException("HR", Failures(1)).Message
            .Should().Contain("must not be deployed");
    }

    /// <summary>
    /// A schema that fails wholesale would otherwise put thousands of lines into one log entry.
    /// </summary>
    [Fact]
    public void Message_WhenThereAreManyFailures_ShouldTruncateAndSayHowManyAreLeft()
    {
        // Arrange
        var exception = new SqlServerDdlGenerationException("HR", Failures(25));

        // Act
        var message = exception.Message;

        // Assert
        message.Should().Contain("25 object(s)")
               .And.Contain("[dbo].[T20]---TABLE")
               .And.NotContain("[dbo].[T21]---TABLE")
               .And.Contain("... and 5 more");
    }

    [Fact]
    public void Message_WhenNothingIsTruncated_ShouldNotSayThereIsMore()
    {
        new SqlServerDdlGenerationException("HR", Failures(20)).Message
            .Should().NotContain("more");
    }

    [Fact]
    public void Failures_ShouldExposeWhatCouldNotBeScripted()
    {
        new SqlServerDdlGenerationException("HR", Failures(3)).Failures
            .Should().HaveCount(3)
            .And.Contain(("[dbo].[T2]---TABLE", "Msg 2: something"));
    }
}
