using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.Tests;

/// <summary>
/// Keeps what was logged so that a test can assert on a warning, and still writes everything to the
/// test output the way <see cref="TestLogger"/> does.
/// </summary>
public sealed class RecordingLogger<T> : ILogger<T>
{
    public IList<(LogLevel Level, string Message)> Entries { get; } = new List<(LogLevel, string)>();

    public IEnumerable<string> Warnings =>
        Entries.Where(entry => entry.Level == LogLevel.Warning).Select(entry => entry.Message);

    public IDisposable BeginScope<TState>(TState state) where TState : notnull =>
        TestLogger.Instance.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add((logLevel, formatter(state, exception)));
        TestLogger.Instance.Log(logLevel, eventId, state, exception, formatter);
    }
}

/// <summary>
/// Hands the same logger to every category, so that a <see cref="DatabaseLoggerFactory"/> built on
/// it records what is written about a database too, and not only what the service writes directly.
/// </summary>
public sealed class RecordingLoggerFactory : ILoggerFactory
{
    private readonly ILogger _logger;

    public RecordingLoggerFactory(ILogger logger)
    {
        _logger = logger;
    }

    public ILogger CreateLogger(string categoryName) => _logger;

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }
}
