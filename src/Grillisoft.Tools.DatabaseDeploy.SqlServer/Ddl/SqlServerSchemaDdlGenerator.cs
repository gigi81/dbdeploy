using System.Data.Common;
using System.Diagnostics;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Database;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer.Ddl;

/// <summary>
/// Scripts a whole SQL Server database into a single deployable file.
/// </summary>
/// <remarks>
/// The work is split three ways: <see cref="SqlServerObjectsDiscovery"/> decides what to script and
/// what depends on what, <see cref="DbObjectsGraph"/> turns that into an order, and
/// <see cref="SqlServerObjectScripter"/> writes the DDL of one object at a time through SMO.
/// Everything here is the orchestration and the writing. Two rules drive the whole design - the
/// script has to be replayable top to bottom on an empty database, and no single object that cannot
/// be scripted may take the whole run down.
/// </remarks>
internal sealed class SqlServerSchemaDdlGenerator
{
    private readonly Func<string, DbCommand> _createCommand;
    private readonly string _connectionString;
    private readonly string _databaseName;
    private readonly string _migrationTable;
    private readonly ILogger _logger;

    private readonly List<(SqlServerObject Object, string Error)> _failures = [];
    private readonly Dictionary<string, int> _scriptedByType = new(StringComparer.OrdinalIgnoreCase);
    private int _statementsWritten;

    public SqlServerSchemaDdlGenerator(
        Func<string, DbCommand> createCommand,
        string connectionString,
        string databaseName,
        string migrationTable,
        ILogger logger)
    {
        _createCommand = createCommand;
        _connectionString = connectionString;
        _databaseName = databaseName;
        _migrationTable = migrationTable;
        _logger = logger;
    }

    public async Task Generate(StreamWriter writer, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("Generating DDL for database {DatabaseName}", _databaseName);

        using var scripter = new SqlServerObjectScripter(_connectionString, _databaseName, _logger);
        scripter.Connect();

        var catalog = new CatalogReader(_createCommand, _databaseName, _logger);
        var discovery = new SqlServerObjectsDiscovery(catalog, _databaseName, _migrationTable, _logger);
        var (objects, dependencies) = await discovery.Discover(cancellationToken);

        if (objects.Count == 0)
        {
            _logger.LogWarning("No scriptable object found in database {DatabaseName}; the generated script will be empty",
                _databaseName);
            await WriteHeader(writer, 0);
            return;
        }

        _logger.LogInformation("Ordering {ObjectCount} objects using {DependencyCount} dependencies",
            objects.Count, dependencies.Count);

        var graph = new DbObjectsGraph(objects, dependencies, SqlServerObjectType.RankOf, _logger);
        var ordered = graph.GetGraph();

        if (graph.IgnoredDependencies > 0)
        {
            _logger.LogInformation(
                "Ignored {IgnoredCount} dependencies pointing at objects that are not being scripted (run with debug logging to list them)",
                graph.IgnoredDependencies);
        }

        await WriteHeader(writer, ordered.Count);

        _logger.LogInformation("Scripting {ObjectCount} objects", ordered.Count);
        var progress = new ProgressReporter(ordered.Count, _logger);

        foreach (var dbObject in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WriteObject(writer, scripter, discovery, dbObject);
            progress.Advance(dbObject);
        }

        await WriteFooter(writer, graph);

        LogSummary(stopwatch.Elapsed, ordered.Count);

        if (_failures.Count > 0)
            throw new SqlServerDdlGenerationException(_databaseName, _failures.Select(f => (f.Object.Key, f.Error)));
    }

    private async Task WriteObject(
        StreamWriter writer,
        SqlServerObjectScripter scripter,
        SqlServerObjectsDiscovery discovery,
        DbObject dbObject)
    {
        if (discovery.Find(dbObject) is not { } target)
        {
            _logger.LogWarning("Skipping {ObjectKey}: it is not one of the discovered objects", dbObject.Key);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        IReadOnlyList<string> batches;

        try
        {
            batches = scripter.Script(target);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var error = ex.Describe();
            _logger.LogError(ex, "Failed to script {ObjectKey}: {Error}", dbObject.Key, error);
            _failures.Add((target, error));
            await writer.WriteComment($"!! FAILED to script {target.Type.Name} {target.QualifiedName}: {error}");
            return;
        }

        if (batches.Count == 0)
        {
            _logger.LogWarning("Skipping {ObjectKey}: SMO returned no statement", dbObject.Key);
            return;
        }

        foreach (var batch in batches)
            await writer.WriteStatement(batch);

        _statementsWritten += batches.Count;
        _scriptedByType[dbObject.Type] = _scriptedByType.GetValueOrDefault(dbObject.Type) + 1;

        _logger.LogDebug("Scripted {ObjectKey} into {BatchCount} batch(es) in {ElapsedMs}ms",
            dbObject.Key, batches.Count, stopwatch.ElapsedMilliseconds);
    }

    private async Task WriteHeader(StreamWriter writer, int objectCount)
    {
        await writer.WriteComment(new string('=', 78));
        await writer.WriteComment($"Database {_databaseName} - {objectCount} object(s)");
        await writer.WriteComment($"Generated by dbdeploy on {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        await writer.WriteComment("Do not edit: regenerate instead");
        await writer.WriteComment(new string('=', 78));
        await writer.WriteLineAsync();
    }

    private async Task WriteFooter(StreamWriter writer, DbObjectsGraph graph)
    {
        if (graph.BrokenCycles.Count == 0 && _failures.Count == 0)
            return;

        await writer.WriteLineAsync();
        await writer.WriteComment(new string('=', 78));

        foreach (var cycle in graph.BrokenCycles)
        {
            await writer.WriteComment(
                $"Dependency cycle, objects may be created invalid: {string.Join(" -> ", cycle.Select(o => o.Key))}");
        }

        foreach (var (dbObject, error) in _failures)
            await writer.WriteComment($"!! {dbObject.Key} could not be scripted: {error}");

        await writer.WriteComment(new string('=', 78));
    }

    private void LogSummary(TimeSpan elapsed, int objectCount)
    {
        _logger.LogInformation(
            "Scripted {ScriptedCount}/{ObjectCount} objects into {StatementCount} batches in {Elapsed}: {Breakdown}",
            _scriptedByType.Values.Sum(), objectCount, _statementsWritten, elapsed,
            _scriptedByType.Breakdown(SqlServerObjectType.RankOf));

        if (_failures.Count > 0)
        {
            _logger.LogError("{FailureCount} object(s) could not be scripted: {Objects}",
                _failures.Count, string.Join(", ", _failures.Select(f => f.Object.Key)));
        }
    }
}
