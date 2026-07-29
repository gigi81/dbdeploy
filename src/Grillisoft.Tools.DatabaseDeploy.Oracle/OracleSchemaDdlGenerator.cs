using System.Data.Common;
using System.Diagnostics;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Database;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle;

/// <summary>
/// Scripts a whole Oracle schema into a single deployable file.
/// </summary>
/// <remarks>
/// The work is split three ways: <see cref="OracleObjectsDiscovery"/> decides what to script and
/// what depends on what, <see cref="OracleObjectsGraph"/> turns that into an order, and
/// <see cref="OracleObjectScripter"/> writes the DDL of one object at a time through
/// <c>DBMS_METADATA</c>. Everything here is the orchestration and the writing. Two rules drive the
/// whole design - the script has to be replayable top to bottom on an empty schema, and no single
/// object that cannot be scripted may take the whole run down.
/// </remarks>
internal sealed class OracleSchemaDdlGenerator
{
    private readonly Func<string, DbCommand> _createCommand;
    private readonly string _schema;
    private readonly string _migrationTable;
    private readonly ILogger _logger;

    private readonly List<(DbObject Object, string Error)> _failures = [];
    private readonly Dictionary<string, int> _scriptedByType = new(StringComparer.OrdinalIgnoreCase);
    private int _statementsWritten;

    public OracleSchemaDdlGenerator(
        Func<string, DbCommand> createCommand,
        string schema,
        string migrationTable,
        ILogger logger)
    {
        _createCommand = createCommand;
        _schema = schema;
        _migrationTable = migrationTable;
        _logger = logger;
    }

    public async Task Generate(StreamWriter writer, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("Generating DDL for schema {SchemaName}", _schema);

        var catalog = new CatalogReader(_createCommand, _schema, _logger);
        var scripter = new OracleObjectScripter(_createCommand, catalog, _schema, _logger);
        await scripter.Configure(cancellationToken);

        var discovery = new OracleObjectsDiscovery(catalog, _schema, _migrationTable, _logger);
        var (objects, dependencies) = await discovery.Discover(cancellationToken);

        if (objects.Count == 0)
        {
            _logger.LogWarning("No scriptable object found in schema {SchemaName}; the generated script will be empty", _schema);
            await WriteHeader(writer, objects.Count);
            return;
        }

        _logger.LogInformation("Ordering {ObjectCount} objects using {DependencyCount} dependencies", objects.Count, dependencies.Count);
        var graph = new OracleObjectsGraph(objects, dependencies, _logger);
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
            await WriteObject(writer, scripter, dbObject, cancellationToken);
            progress.Advance(dbObject);
        }

        await WriteComments(writer, discovery, ordered, cancellationToken);
        await WriteFooter(writer, graph);

        LogSummary(stopwatch.Elapsed, ordered.Count, scripter.FallbacksUsed);

        if (_failures.Count > 0)
            throw new OracleDdlGenerationException(_schema, _failures.Select(f => (f.Object.Key, f.Error)));
    }

    private async Task WriteObject(
        StreamWriter writer,
        OracleObjectScripter scripter,
        DbObject dbObject,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        IReadOnlyList<string> statements;

        try
        {
            statements = await scripter.Script(dbObject, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var error = ex.Describe();
            _logger.LogError(ex, "Failed to script {ObjectKey}: {Error}", dbObject.Key, error);
            _failures.Add((dbObject, error));
            await writer.WriteComment($"!! FAILED to script {dbObject.Type} {dbObject.Name}: {error}");
            return;
        }

        if (statements.Count == 0)
            return;

        foreach (var statement in statements)
            await writer.WriteStatement(statement);

        _statementsWritten += statements.Count;
        _scriptedByType[dbObject.Type] = _scriptedByType.GetValueOrDefault(dbObject.Type) + 1;

        _logger.LogDebug("Wrote {ObjectKey} in {ElapsedMs}ms", dbObject.Key, stopwatch.ElapsedMilliseconds);
    }

    private async Task WriteComments(
        StreamWriter writer,
        OracleObjectsDiscovery discovery,
        IReadOnlyList<DbObject> objects,
        CancellationToken cancellationToken)
    {
        var comments = await discovery.GetComments(objects, cancellationToken);

        foreach (var (table, column, comment) in comments)
        {
            var target = column is null ? table.Quote() : $"{table.Quote()}.{column.Quote()}";
            var what = column is null ? "TABLE" : "COLUMN";

            await writer.WriteStatement($"COMMENT ON {what} {target} IS {comment.ToSqlLiteral()}");
        }

        _statementsWritten += comments.Count;
        _logger.LogInformation("Scripted {CommentCount} comments", comments.Count);
    }

    private async Task WriteHeader(StreamWriter writer, int objectCount)
    {
        await writer.WriteComment(new string('=', 78));
        await writer.WriteComment($"Schema {_schema} - {objectCount} object(s)");
        await writer.WriteComment($"Generated by dbdeploy on {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        await writer.WriteComment("Do not edit: regenerate instead");
        await writer.WriteComment(new string('=', 78));
        await writer.WriteLineAsync();
    }

    private async Task WriteFooter(StreamWriter writer, OracleObjectsGraph graph)
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

    private void LogSummary(TimeSpan elapsed, int objectCount, int fallbacksUsed)
    {
        _logger.LogInformation(
            "Scripted {ScriptedCount}/{ObjectCount} objects into {StatementCount} statements in {Elapsed}: {Breakdown}",
            _scriptedByType.Values.Sum(), objectCount, _statementsWritten, elapsed,
            _scriptedByType.Breakdown(OracleObjectType.RankOf));

        if (fallbacksUsed > 0)
        {
            _logger.LogWarning(
                "{FallbackCount} object(s) were rebuilt from the data dictionary because DBMS_METADATA refused them; " +
                "granting SELECT_CATALOG_ROLE to the connected user produces a more faithful script",
                fallbacksUsed);
        }

        if (_failures.Count > 0)
        {
            _logger.LogError("{FailureCount} object(s) could not be scripted: {Objects}",
                _failures.Count, string.Join(", ", _failures.Select(f => f.Object.Key)));
        }
    }
}
