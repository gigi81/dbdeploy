using AwesomeAssertions;
using Grillisoft.Tools.DatabaseDeploy.Oracle.Ddl;
using Xunit;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle.Tests.Ddl;

public class OracleDdlGenerationExceptionTests
{
    private static IEnumerable<(string Object, string Error)> Failures(int count)
        => Enumerable.Range(1, count).Select(i => ($"PKG{i}---PACKAGE BODY", $"ORA-3160{i}: something"));

    [Fact]
    public void Message_ShouldNameTheSchemaAndEveryFailure()
    {
        // Arrange
        var exception = new OracleDdlGenerationException("HR", Failures(2));

        // Act
        var message = exception.Message;

        // Assert
        message.Should().Contain("2 object(s) of schema HR")
               .And.Contain("PKG1---PACKAGE BODY: ORA-31601: something")
               .And.Contain("PKG2---PACKAGE BODY: ORA-31602: something");
    }

    /// <summary>
    /// The script is written out in full before this is raised, so the message has to be clear that
    /// what is on disk is not deployable.
    /// </summary>
    [Fact]
    public void Message_ShouldSayTheScriptMustNotBeDeployed()
    {
        new OracleDdlGenerationException("HR", Failures(1)).Message
            .Should().Contain("must not be deployed");
    }

    /// <summary>
    /// A schema that fails wholesale would otherwise put thousands of lines into one log entry.
    /// </summary>
    [Fact]
    public void Message_WhenThereAreManyFailures_ShouldTruncateAndSayHowManyAreLeft()
    {
        // Arrange
        var exception = new OracleDdlGenerationException("HR", Failures(25));

        // Act
        var message = exception.Message;

        // Assert
        message.Should().Contain("25 object(s)")
               .And.Contain("PKG20---PACKAGE BODY")
               .And.NotContain("PKG21---PACKAGE BODY")
               .And.Contain("... and 5 more");
    }

    [Fact]
    public void Message_WhenNothingIsTruncated_ShouldNotSayThereIsMore()
    {
        new OracleDdlGenerationException("HR", Failures(20)).Message
            .Should().NotContain("more");
    }

    [Fact]
    public void Failures_ShouldExposeWhatCouldNotBeScripted()
    {
        new OracleDdlGenerationException("HR", Failures(3)).Failures
            .Should().HaveCount(3)
            .And.Contain(("PKG2---PACKAGE BODY", "ORA-31602: something"));
    }
}
