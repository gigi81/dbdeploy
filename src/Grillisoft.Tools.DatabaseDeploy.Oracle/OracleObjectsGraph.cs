using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle;

/// <summary>
/// Orders the objects of a schema so that every object is written to the script after the objects
/// it depends on.
/// </summary>
/// <remarks>
/// This is a Kahn topological sort where the tie between objects that are all ready to be written
/// is broken by <see cref="OracleObjectType.RankOf"/> and then by name, which keeps the output
/// stable between runs and keeps objects of the same kind together.
/// <para>
/// Two things a real schema does that a naive sort cannot survive: dependencies pointing at objects
/// that are not being scripted (objects of an unsupported type, objects filtered out, objects
/// dropped between the two queries) and dependency cycles, which are perfectly legal between PL/SQL
/// bodies. Both are tolerated and reported instead of aborting the generation.
/// </para>
/// </remarks>
public sealed class OracleObjectsGraph
{
    private static readonly IComparer<DbObject> ScriptOrder = new ScriptOrderComparer();

    private readonly ILogger _logger;
    private readonly List<DbObject> _objects;
    private readonly Dictionary<DbObject, HashSet<DbObject>> _requires;
    private readonly Dictionary<DbObject, HashSet<DbObject>> _requiredBy;
    private readonly List<IReadOnlyList<DbObject>> _brokenCycles = [];

    public OracleObjectsGraph(
        IEnumerable<DbObject> dbObjects,
        IEnumerable<OracleObjectDependencies> dbObjectsDependencies,
        ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
        _objects = dbObjects.Distinct().ToList();
        _requires = _objects.ToDictionary(o => o, _ => new HashSet<DbObject>());
        _requiredBy = _objects.ToDictionary(o => o, _ => new HashSet<DbObject>());

        var known = _requires.Keys.ToHashSet();

        foreach (var dependency in dbObjectsDependencies)
        {
            if (!known.TryGetValue(dependency.DbObject, out var dependent))
            {
                _logger.LogDebug(
                    "Ignoring dependency {ObjectKey} -> {DependencyKey}: {MissingKey} is not being scripted",
                    dependency.DbObject.Key, dependency.DbObjectDependency.Key, dependency.DbObject.Key);
                IgnoredDependencies++;
                continue;
            }

            if (!known.TryGetValue(dependency.DbObjectDependency, out var dependsOn))
            {
                _logger.LogDebug(
                    "Ignoring dependency {ObjectKey} -> {DependencyKey}: {MissingKey} is not being scripted",
                    dependency.DbObject.Key, dependency.DbObjectDependency.Key, dependency.DbObjectDependency.Key);
                IgnoredDependencies++;
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
        var ready = new SortedSet<DbObject>(ScriptOrder);
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

    /// <summary>
    /// Nothing is ready but objects are left: everything still pending is part of, or blocked by,
    /// a cycle. Report the cycle and release its lowest ranked member so the sort can carry on.
    /// </summary>
    private void BreakCycle(Dictionary<DbObject, HashSet<DbObject>> pending, SortedSet<DbObject> ready)
    {
        var cycle = FindCycle(pending) ?? [pending.Keys.Min(ScriptOrder)!];
        var victim = cycle.Min(ScriptOrder)!;

        _brokenCycles.Add(cycle);

        _logger.LogWarning(
            "Dependency cycle between {ObjectCount} object(s): {Cycle}. Scripting {Victim} first; " +
            "objects in the cycle may be created invalid and will be recompiled by Oracle on first use",
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
    private static List<DbObject>? FindCycle(Dictionary<DbObject, HashSet<DbObject>> pending)
    {
        const int visiting = 1;
        const int visited = 2;

        var state = new Dictionary<DbObject, int>();

        foreach (var start in pending.Keys.OrderBy(o => o, ScriptOrder))
        {
            if (state.ContainsKey(start))
                continue;

            var path = new List<DbObject>();
            var stack = new Stack<(DbObject Node, IEnumerator<DbObject> Requires)>();

            state[start] = visiting;
            path.Add(start);
            stack.Push((start, pending[start].OrderBy(o => o, ScriptOrder).GetEnumerator()));

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
                stack.Push((next, nextRequires.OrderBy(o => o, ScriptOrder).GetEnumerator()));
            }
        }

        return null;
    }

    private sealed class ScriptOrderComparer : IComparer<DbObject>
    {
        public int Compare(DbObject? x, DbObject? y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x is null)
                return -1;
            if (y is null)
                return 1;

            var compare = OracleObjectType.RankOf(x.Type).CompareTo(OracleObjectType.RankOf(y.Type));
            if (compare != 0)
                return compare;

            compare = string.CompareOrdinal(x.Type, y.Type);
            return compare != 0 ? compare : string.CompareOrdinal(x.Name, y.Name);
        }
    }
}
