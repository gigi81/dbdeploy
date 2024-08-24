using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy;

public class DatabaseLogger : ILogger
{
    private readonly string _database;
    private readonly ILogger _logger;

    public DatabaseLogger(string database, ILogger logger)
    {
        _database = database;
        _logger = logger;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _logger.Log(logLevel, eventId, state, exception, GetFormatter(formatter));
    }

    private Func<TState,Exception?,string> GetFormatter<TState>(Func<TState,Exception?,string> formatter)
    {
        return (state, exception) => $"Database {_database}: " + formatter(state, exception);
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return _logger.IsEnabled(logLevel);
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return _logger.BeginScope(state);
    }
}