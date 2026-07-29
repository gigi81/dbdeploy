using AwesomeAssertions;
using Grillisoft.Tools.DatabaseDeploy.SqlServer.Ddl;
using Xunit;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer.Tests.Ddl;

public class ExceptionExtensionsTests
{
    [Fact]
    public void Describe_ShouldUseTheMessage()
    {
        new InvalidOperationException("something went wrong").Describe()
            .Should().Be("something went wrong");
    }

    [Fact]
    public void Describe_ShouldTrimTheMessage()
    {
        new InvalidOperationException("  padded  \n").Describe().Should().Be("padded");
    }

    /// <summary>
    /// This is the whole point of the method: SMO wraps the reason the server gave in a
    /// FailedOperationException around an ExecutionFailureException, and the outer messages say
    /// nothing a user can act on.
    /// </summary>
    [Fact]
    public void Describe_WhenTheExceptionIsWrapped_ShouldReachTheInnermostOne()
    {
        // Arrange
        var exception = new InvalidOperationException(
            "Script failed for Table 'dbo.T1'",
            new InvalidOperationException(
                "An exception occurred while executing a Transact-SQL statement",
                new InvalidOperationException("Invalid column name 'Missing'")));

        // Act
        var described = exception.Describe();

        // Assert
        described.Should().Be("Invalid column name 'Missing'");
    }
}
