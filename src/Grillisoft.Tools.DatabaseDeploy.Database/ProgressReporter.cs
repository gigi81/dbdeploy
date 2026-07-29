using System.Diagnostics;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.Database;

/// <summary>
/// Periodic progress over a run that scripts objects one at a time, so a database with thousands of
/// them does not look hung.
/// </summary>
/// <remarks>
/// Reports twenty times over the run, and never more often than every twenty five objects, so that
/// a small schema produces one line and a large one produces twenty rather than one per object.
/// </remarks>
/// <param name="total">How many objects will be advanced through, at least one.</param>
public sealed class ProgressReporter(int total, ILogger logger)
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly int _interval = Math.Max(25, total / 20);
    private int _done;

    /// <summary>Records one more object as done, and reports if this one falls on the interval.</summary>
    public void Advance(DbObject dbObject)
    {
        _done++;

        if (_done % _interval != 0 && _done != total)
            return;

        logger.LogInformation("Scripted {Done}/{Total} objects ({Percent}%) in {Elapsed}, last was {ObjectKey}",
            _done, total, _done * 100 / total, _stopwatch.Elapsed, dbObject.Key);
    }
}
