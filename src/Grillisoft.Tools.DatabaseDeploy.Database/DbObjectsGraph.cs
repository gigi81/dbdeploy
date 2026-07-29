using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.Database;

/// <summary>
/// Orders the objects of a schema so that every object is written to the script after the objects
/// it depends on.
/// </summary>
/// <remarks>
/// This is a Kahn topological sort where the tie between objects that are all ready to be written
/// is broken by the rank of their type and then by name, which keeps the output stable between runs
/// and keeps objects of the same kind together.
/// <para>
/// Two things a real database does that a naive sort cannot survive: dependencies pointing at
/// objects that are not being scripted (objects of an unsupported type, objects filtered out,
/// objects dropped between two queries) and dependency cycles, which every engine allows between
/// program units. Both are tolerated and reported instead of aborting the generation.
/// </para>
/// </remarks>
public class DbObjectsGraph
{
    private readonly IComparer<DbObject> _scriptOrder;
    private readonly ILogger _logger;
    private readonly List<DbObject> _objects;
    private readonly Dictionary<DbObject, HashSet<DbObject>> _requires;
    private readonly Dictionary<DbObject, HashSet<DbObject>> _requiredBy;
    private readonly List<IReadOnlyList<DbObject>> _brokenCycles = [];

    /// <param name="dbObjects">The objects to order. Duplicates are collapsed.</param>
    /// <param name="dependencies">
    /// Pairs where the first object can only be created once the second one exists.
    /// </param>
    /// <param name="rankOf">
    /// Position of an object type in the script, used to break the tie between objects that are all
    /// ready to be written. Lower comes first.
    /// </param>
    /// <param name="logger">Optional logger; every decision the sort takes is reported through it.</param>
    public DbObjectsGraph(
        IEnumerable<DbObject> dbObjects,
        IEnumerable<(DbObject DbObject, DbObject DependsOn)> dependencies,
        Func<string, int> rankOf,
        ILogger? logger = null)
    {
        _scriptOrder = new ScriptOrderComparer(rankOf);
        _logger = logger ?? NullLogger.Instance;
        _objects = dbObjects.Distinct().ToList();
        _requires = _objects.ToDictionary(o => o, _ => new HashSet<DbObject>());
        _requiredBy = _objects.ToDictionary(o => o, _ => new HashSet<DbObject>());

        var known = _requires.Keys.ToHashSet();

        foreach (var (dbObject, dependsOnObject) in dependencies)
        {
            if (!known.TryGetValue(dbObject, out var dependent))
            {
                LogIgnored(dbObject, dependsOnObject, dbObject);
                continue;
            }

            if (!known.TryGetValue(dependsOnObject, out var dependsOn))
            {
                LogIgnored(dbObject, dependsOnObject, dependsOnObject);
                continue;
            }

            if (dependent.Equals(dependsOn))
                continue;

            if (_requires[dependent].Add(dependsOn))
                _requiredBy[dependsOn].Add(dependent);
        }
    }

    /// <summary>
    /// Number of dependencies discarded because one of the two ends is not part of the objects
    /// being scripted.
    /// </summary>
    public int IgnoredDependencies { get; private set; }

    /// <summary>
    /// Cycles that had to be broken to produce an order. Objects involved in a cycle may be created
    /// invalid and need a recompilation.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<DbObject>> BrokenCycles => _brokenCycles;

    public IReadOnlyList<DbObject> GetGraph()
    {
        var pending = _requires.ToDictionary(kv => kv.Key, kv => new HashSet<DbObject>(kv.Value));
        var ready = new SortedSet<DbObject>(_scriptOrder);
        var result = new List<DbObject>(_objects.Count);

        foreach (var (dbObject, requires) in pending)
        {
            if (requires.Count == 0)
                ready.Add(dbObject);
        }

        while (result.Count < _objects.Count)
        {
            if (ready.Count == 0)
            {
                BreakCycle(pending, ready);
                continue;
            }

            var next = ready.Min!;
            ready.Remove(next);
            pending.Remove(next);
            result.Add(next);

            foreach (var dependent in _requiredBy[next])
            {
                if (pending.TryGetValue(dependent, out var requires) && requires.Remove(next) && requires.Count == 0)
                    ready.Add(dependent);
            }
        }

        _logger.LogDebug("Ordered {Count} objects, breaking {CycleCount} dependency cycle(s)",
            result.Count, _brokenCycles.Count);

        return result;
    }

    private void LogIgnored(DbObject dbObject, DbObject dependsOn, DbObject missing)
    {
        _logger.LogDebug(
            "Ignoring dependency {ObjectKey} -> {DependencyKey}: {MissingKey} is not being scripted",
            dbObject.Key, dependsOn.Key, missing.Key);

        IgnoredDependencies++;
    }

    /// <summary>
    /// Nothing is ready but objects are left: everything still pending is part of, or blocked by,
    /// a cycle. Report the cycle and release its lowest ranked member so the sort can carry on.
    /// </summary>
    private void BreakCycle(Dictionary<DbObject, HashSet<DbObject>> pending, SortedSet<DbObject> ready)
    {
        var cycle = FindCycle(pending) ?? [pending.Keys.Min(_scriptOrder)!];
        var victim = cycle.Min(_scriptOrder)!;

        _brokenCycles.Add(cycle);

        _logger.LogWarning(
            "Dependency cycle between {ObjectCount} object(s): {Cycle}. Scripting {Victim} first; " +
            "objects in the cycle may be created invalid and will need a recompilation",
            cycle.Count,
            string.Join(" -> ", cycle.Select(o => o.Key).Append(cycle[0].Key)),
            victim.Key);

        pending[victim].Clear();
        ready.Add(victim);
    }

    /// <summary>
    /// Depth first search over the objects still pending, returning the first cycle found.
    /// Iterative on purpose: schemas with tens of thousands of objects would blow the stack.
    /// </summary>
    private List<DbObject>? FindCycle(Dictionary<DbObject, HashSet<DbObject>> pending)
    {
        const int visiting = 1;
        const int visited = 2;

        var state = new Dictionary<DbObject, int>();

        foreach (var start in pending.Keys.OrderBy(o => o, _scriptOrder))
        {
            if (state.ContainsKey(start))
                continue;

            var path = new List<DbObject>();
            var stack = new Stack<(DbObject Node, IEnumerator<DbObject> Requires)>();

            state[start] = visiting;
            path.Add(start);
            stack.Push((start, pending[start].OrderBy(o => o, _scriptOrder).GetEnumerator()));

            while (stack.Count > 0)
            {
                var (node, requires) = stack.Peek();

                if (!requires.MoveNext())
                {
                    requires.Dispose();
                    stack.Pop();
                    state[node] = visited;
                    path.RemoveAt(path.Count - 1);
                    continue;
                }

                var next = requires.Current;

                // Already scripted, or not part of the pending set: cannot close a cycle.
                if (!pending.TryGetValue(next, out var nextRequires))
                    continue;

                if (state.TryGetValue(next, out var nextState))
                {
                    if (nextState != visiting)
                        continue;

                    // Closed a loop: everything from `next` to the top of the path is the cycle.
                    var from = path.IndexOf(next);
                    var cycle = path.GetRange(from, path.Count - from);

                    while (stack.Count > 0)
                        stack.Pop().Requires.Dispose();

                    return cycle;
                }

                state[next] = visiting;
                path.Add(next);
                stack.Push((next, nextRequires.OrderBy(o => o, _scriptOrder).GetEnumerator()));
            }
        }

        return null;
    }

    private sealed class ScriptOrderComparer(Func<string, int> rankOf) : IComparer<DbObject>
    {
        public int Compare(DbObject? x, DbObject? y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x is null)
                return -1;
            if (y is null)
                return 1;

            var compare = rankOf(x.Type).CompareTo(rankOf(y.Type));
            if (compare != 0)
                return compare;

            compare = string.CompareOrdinal(x.Type, y.Type);
            return compare != 0 ? compare : string.CompareOrdinal(x.Name, y.Name);
        }
    }
}
