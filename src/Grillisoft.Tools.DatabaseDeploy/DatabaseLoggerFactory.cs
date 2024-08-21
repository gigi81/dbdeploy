using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy;

public class DatabaseLoggerFactory
{
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, DatabaseLogger> _loggers = new();
    
    public DatabaseLoggerFactory(ILogger logger)
    {
        _logger = logger;
    }

    public ILogger this[string databaseName] =>
        _loggers.GetOrAdd(databaseName.ToLowerInvariant(), n => new DatabaseLogger(n, _logger));
}