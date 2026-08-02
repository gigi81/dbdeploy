using System.Diagnostics;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.Database.Ddl;

/// <summary>
/// Scripts a whole schema or database into a single deployable file.
/// </summary>
/// <remarks>
/// Every provider splits the work the same three ways: a discovery decides what to script and what
/// depends on what, <see cref="DbObjectsGraph"/> turns that into an order, and a scripter writes the
/// DDL of one object at a time. This class is the orchestration and the writing, which is the same
/// everywhere; a provider contributes the three pieces that are not. Two rules drive the whole
/// design - the script has to be replayable top to bottom on an empty database, and no single object
/// that cannot be scripted may take the whole run down.
/// </remarks>
public abstract class SchemaDdlGenerator
{
    private readonly string _sourceKind;
    private readonly List<(DbObject Object, string Error)> _failures = [];
    private readonly Dictionary<string, int> _scriptedByType = new(StringComparer.OrdinalIgnoreCase);
    private int _statementsWritten;

    /// <param name="source">The schema or database being scripted.</param>
    /// <param name="sourceKind">What <paramref name="source"/> is: "schema" or "database".</param>
    protected SchemaDdlGenerator(string source, string sourceKind, ILogger logger)
    {
        Source = source;
        _sourceKind = sourceKind;
        Logger = logger;
    }

    protected string Source { get; }

    protected ILogger Logger { get; }

    /// <summary>The objects that could not be scripted, in the order they were attempted.</summary>
    protected IReadOnlyList<(DbObject Object, string Error)> Failures => _failures;

    /// <summary>
    /// Position of an object type in the script, used to break the tie between objects the
    /// dependencies leave free to go in any order.
    /// </summary>
    protected abstract Func<string, int> RankOf { get; }

    protected abstract DdlScriptWriter CreateWriter(StreamWriter stream);

    /// <summary>What to script, and what has to exist before what.</summary>
    protected abstract Task<(List<DbObject> Objects, List<(DbObject DbObject, DbObject DependsOn)> Dependencies)>
        Discover(CancellationToken cancellationToken);

    /// <summary>
    /// The statements that create one object, or an empty list when there is nothing to write for
    /// it. Throwing is how an object is reported as a failure.
    /// </summary>
    protected abstract Task<IReadOnlyList<string>> Script(DbObject dbObject, CancellationToken cancellationToken);

    /// <summary>
    /// Session setup done once before anything is read: connecting a scripter, or talking the server
    /// into emitting portable DDL.
    /// </summary>
    protected virtual Task Prepare(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Written after the last object and before the footer, for the statements that belong to no
    /// object of their own - comments, mostly. Implementations report what they wrote through
    /// <see cref="CountStatements"/>.
    /// </summary>
    protected virtual Task WriteEpilogue(
        DdlScriptWriter writer,
        IReadOnlyList<DbObject> ordered,
        CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Anything the provider wants to add to the summary logged at the end of a run.</summary>
    protected virtual void LogSummaryDetails()
    {
    }

    /// <summary>What a statement is called in the logs of this dialect.</summary>
    protected virtual string StatementNoun => "statements";

    /// <summary>The one line worth reading of the exception behind a failed object.</summary>
    protected virtual string Describe(Exception exception) => exception.Message.Trim();

    protected virtual Exception CreateGenerationException(IEnumerable<(string Object, string Error)> failures)
        => new DdlGenerationException(Source, _sourceKind, failures);

    /// <summary>Records statements written outside the object loop, so the summary adds up.</summary>
    protected void CountStatements(int count) => _statementsWritten += count;

    public async Task Generate(StreamWriter stream, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        Logger.LogInformation("Generating DDL for {SourceKind} {Source}", _sourceKind, Source);

        var writer = CreateWriter(stream);
        await Prepare(cancellationToken);

        var (objects, dependencies) = await Discover(cancellationToken);

        if (objects.Count == 0)
        {
            Logger.LogWarning("No scriptable object found in {SourceKind} {Source}; the generated script will be empty",
                _sourceKind, Source);
            await WriteHeader(writer, 0);
            return;
        }

        Logger.LogInformation("Ordering {ObjectCount} objects using {DependencyCount} dependencies",
            objects.Count, dependencies.Count);

        var graph = new DbObjectsGraph(objects, dependencies, RankOf, Logger);
        var ordered = graph.GetGraph();

        if (graph.IgnoredDependencies > 0)
        {
            Logger.LogInformation(
                "Ignored {IgnoredCount} dependencies pointing at objects that are not being scripted (run with debug logging to list them)",
                graph.IgnoredDependencies);
        }

        await WriteHeader(writer, ordered.Count);

        Logger.LogInformation("Scripting {ObjectCount} objects", ordered.Count);
        var progress = new ProgressReporter(ordered.Count, Logger);

        foreach (var dbObject in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WriteObject(writer, dbObject, cancellationToken);
            progress.Advance(dbObject);
        }

        await WriteEpilogue(writer, ordered, cancellationToken);
        await WriteFooter(writer, graph);

        LogSummary(stopwatch.Elapsed, ordered.Count);

        if (_failures.Count > 0)
            throw CreateGenerationException(_failures.Select(f => (f.Object.Key, f.Error)));
    }

    private async Task WriteObject(DdlScriptWriter writer, DbObject dbObject, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        IReadOnlyList<string> statements;

        try
        {
            statements = await Script(dbObject, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var error = Describe(ex);
            Logger.LogError(ex, "Failed to script {ObjectKey}: {Error}", dbObject.Key, error);
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

        Logger.LogDebug("Scripted {ObjectKey} into {StatementCount} statement(s) in {ElapsedMs}ms",
            dbObject.Key, statements.Count, stopwatch.ElapsedMilliseconds);
    }

    private async Task WriteHeader(DdlScriptWriter writer, int objectCount)
    {
        await writer.WriteComment(new string('=', 78));
        await writer.WriteComment($"{char.ToUpperInvariant(_sourceKind[0])}{_sourceKind[1..]} {Source} - {objectCount} object(s)");
        await writer.WriteComment($"Generated by dbdeploy on {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        await writer.WriteComment("Do not edit: regenerate instead");
        await writer.WriteComment(new string('=', 78));
        await writer.WriteLine();
    }

    private async Task WriteFooter(DdlScriptWriter writer, DbObjectsGraph graph)
    {
        if (graph.BrokenCycles.Count == 0 && _failures.Count == 0)
            return;

        await writer.WriteLine();
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
        Logger.LogInformation(
            "Scripted {ScriptedCount}/{ObjectCount} objects into {StatementCount} {StatementNoun} in {Elapsed}: {Breakdown}",
            _scriptedByType.Values.Sum(), objectCount, _statementsWritten, StatementNoun, elapsed,
            _scriptedByType.Breakdown(RankOf));

        LogSummaryDetails();

        if (_failures.Count > 0)
        {
            Logger.LogError("{FailureCount} object(s) could not be scripted: {Objects}",
                _failures.Count, string.Join(", ", _failures.Select(f => f.Object.Key)));
        }
    }
}
