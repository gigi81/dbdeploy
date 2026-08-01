using System.Collections.Concurrent;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy;

public class DatabaseLoggerFactory : IDatabaseLoggerFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ConcurrentDictionary<string, DatabaseLogger> _loggers = new();

    public DatabaseLoggerFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public ILogger this[string databaseName] =>
        _loggers.GetOrAdd(databaseName.ToLowerInvariant(), n => new DatabaseLogger(n, _loggerFactory.CreateLogger(n)));
}
