using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy;

public class DatabaseLogger : ILogger
{
    private static Type? FormattedLogValuesType = Type.GetType("Microsoft.Extensions.Logging.FormattedLogValues, Microsoft.Extensions.Logging.Abstractions");

    private readonly string[] _database;
    private readonly ILogger _logger;
    private readonly string _prefixFormat;

    public DatabaseLogger(string database, ILogger logger)
    {
        _database = [database];
        _logger = logger;
        _prefixFormat = "Database {Database}: ";
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;
        
        state = CreateNewState(state);
        _logger.Log(logLevel, eventId, state, exception, formatter);
    }

    /// <summary>
    /// See reason why we have to do this horror here:
    /// https://github.com/dotnet/runtime/issues/67577#issuecomment-2291356471
    /// </summary>
    /// <param name="state"></param>
    /// <typeparam name="TState"></typeparam>
    /// <returns></returns>
    private TState CreateNewState<TState>(TState state)
    {
        if (FormattedLogValuesType == null || typeof(TState) != FormattedLogValuesType)
            return state;

        if (state is not IReadOnlyList<KeyValuePair<string, object>> keyValuePairs)
            return state;

        //the last item of keyValuePairs contains the original format
        var values = _database.Concat(keyValuePairs.Take(keyValuePairs.Count - 1).Select(kvp => kvp.Value)).ToArray();
        var format = _prefixFormat + keyValuePairs[^1].Value;
        
        return (TState?)Activator.CreateInstance(FormattedLogValuesType, format, values) ?? state;
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