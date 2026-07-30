using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.Tests;

/// <summary>
/// Writes to the output of the test that is running, which is what TUnit puts in the report when a
/// test fails. It replaces the xUnit <c>ITestOutputHelper</c> the tests used to be handed: TUnit
/// exposes the current test through <see cref="TestContext"/> instead of through the constructor,
/// so nothing has to be threaded down to the class under test.
/// </summary>
public class TestLogger : ILogger
{
    public static readonly TestLogger Instance = new();

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        // Outside a test - a fixture starting a container, say - there is nowhere to write to.
        var writer = TestContext.Current?.OutputWriter;
        if (writer is null)
            return;

        writer.WriteLine($"[{logLevel}] {formatter(state, exception)}");

        if (exception is not null)
            writer.WriteLine(exception.ToString());
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}

/// <inheritdoc cref="TestLogger"/>
public sealed class TestLogger<T> : TestLogger, ILogger<T>
{
    public static readonly new TestLogger<T> Instance = new();
}

/// <summary>
/// Hands out <see cref="TestLogger"/> to anything that asks for a logger factory.
/// </summary>
public sealed class TestLoggerFactory : ILoggerFactory
{
    public static readonly TestLoggerFactory Instance = new();

    public ILogger CreateLogger(string categoryName) => TestLogger.Instance;

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }
}
