using System.Data.Common;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.Database;

/// <summary>
/// Reads the catalog views a schema generator works its plan out from.
/// </summary>
/// <remarks>
/// The distinction that matters here is <see cref="Query{T}"/> against <see cref="TryQuery{T}"/>.
/// Some of these views are the generation itself - losing the object list means there is nothing to
/// script - while others only make the script better, and a login that is not granted on one of
/// those should get a warning and a slightly poorer script rather than no script at all.
/// </remarks>
public sealed class CatalogReader
{
    private readonly Func<string, DbCommand> _createCommand;
    private readonly string _source;
    private readonly ILogger _logger;

    /// <param name="createCommand">Builds a command on the connection being scripted.</param>
    /// <param name="source">The database or schema being read, for the logs.</param>
    public CatalogReader(Func<string, DbCommand> createCommand, string source, ILogger logger)
    {
        _createCommand = createCommand;
        _source = source;
        _logger = logger;
    }

    /// <summary>Runs a query whose failure there is no way to carry on from.</summary>
    public async Task<List<T>> Query<T>(
        string sql,
        string description,
        Func<DbDataReader, T> read,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        _logger.LogDebug("Reading {Description} from {Source}", description, _source);

        await using var command = _createCommand(sql);
        foreach (var (name, value) in parameters)
            command.AddParameter(name, value);

        var results = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(read(reader));

        return results;
    }

    /// <summary>Same as <see cref="Query{T}"/>, but a failure is only a warning.</summary>
    public async Task<List<T>> TryQuery<T>(
        string sql,
        string description,
        Func<DbDataReader, T> read,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        try
        {
            return await Query(sql, description, read, cancellationToken, parameters);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not read {Description} from {Source}; carrying on without it", description, _source);
            return [];
        }
    }

    /// <summary>
    /// A <see cref="TryQuery{T}"/> over a single name column, for the queries that only exist to
    /// answer "is this one of them?".
    /// </summary>
    /// <param name="comparer">
    /// How the server compares identifiers: ordinal for a case sensitive dictionary like Oracle's,
    /// ignoring case for SQL Server.
    /// </param>
    public async Task<HashSet<string>> TryQueryNames(
        string sql,
        string description,
        StringComparer comparer,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        var names = await TryQuery(sql, description, reader => reader.GetString(0), cancellationToken, parameters);
        return names.ToHashSet(comparer);
    }
}
