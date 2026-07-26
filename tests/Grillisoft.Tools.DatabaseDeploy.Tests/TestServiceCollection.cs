using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.Tests;

public class TestServiceCollection<TSystemUnderTest> : ServiceCollection where TSystemUnderTest : class
{
    public TestServiceCollection(ITestOutputHelper output)
    {
        this.AddSingleton((ILogger<TSystemUnderTest>)output.BuildLoggerFor<TSystemUnderTest>());
        this.AddSingleton((ILogger)output.BuildLoggerFor<TSystemUnderTest>());
        this.AddSingleton<TSystemUnderTest>();
    }
}
