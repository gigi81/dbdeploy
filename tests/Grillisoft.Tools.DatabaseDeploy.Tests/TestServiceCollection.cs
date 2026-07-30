using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.Tests;

public class TestServiceCollection<TSystemUnderTest> : ServiceCollection where TSystemUnderTest : class
{
    public TestServiceCollection()
    {
        this.AddSingleton<ILogger<TSystemUnderTest>>(TestLogger<TSystemUnderTest>.Instance);
        this.AddSingleton<ILogger>(TestLogger.Instance);
        this.AddSingleton<TSystemUnderTest>();
    }
}
