using Grillisoft.Tools.DatabaseDeploy.Oracle.Ddl;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle.Tests.Ddl;

public class ExceptionExtensionsTests
{
    [Test]
    public void Describe_ShouldUseTheMessage()
    {
        new InvalidOperationException("something went wrong").Describe()
            .Should().Be("something went wrong");
    }

    /// <summary>
    /// ODP.NET puts the ORA number on its own line above the text, so the message arrives with a
    /// trailing newline more often than not.
    /// </summary>
    [Test]
    public void Describe_ShouldTrimTheMessage()
    {
        new InvalidOperationException("  padded  \n").Describe().Should().Be("padded");
    }
}
