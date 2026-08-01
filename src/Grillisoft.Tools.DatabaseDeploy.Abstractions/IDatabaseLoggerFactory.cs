using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.Abstractions;

/// <summary>
/// Hands out the logger of a database: the same messages as any other logger, with the database
/// they are about carried along. One instance serves the whole run, so the loggers are shared by
/// everything that writes about a database.
/// </summary>
public interface IDatabaseLoggerFactory
{
    ILogger this[string databaseName] { get; }
}
