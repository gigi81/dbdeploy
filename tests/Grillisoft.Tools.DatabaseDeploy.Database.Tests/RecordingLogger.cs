using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.Database.Tests;

/// <summary>
/// An <see cref="ILogger"/> that keeps what it was told, for the classes whose whole job is to say
/// something useful.
/// </summary>
internal sealed class RecordingLogger : ILogger
{
    public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

    public IEnumerable<string> Messages => Entries.Select(entry => entry.Message);

    public IEnumerable<string> Warnings =>
        Entries.Where(entry => entry.Level == LogLevel.Warning).Select(entry => entry.Message);

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
        => Entries.Add((logLevel, formatter(state, exception), exception));

    public bool IsEnabled(LogLevel logLevel) => true;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
}
