namespace Grillisoft.Tools.DatabaseDeploy.Oracle;

/// <summary>
/// Thrown when the generated script is incomplete because one or more objects could not be
/// scripted. The script is written out in full before this is raised, with the failures repeated in
/// it as comments, so it can be inspected and fixed by hand.
/// </summary>
public class OracleDdlGenerationException : Exception
{
    private const int MaxReported = 20;

    private readonly string _schema;
    private readonly List<(string Object, string Error)> _failures;

    public OracleDdlGenerationException(string schema, IEnumerable<(string Object, string Error)> failures)
    {
        _schema = schema;
        _failures = failures.ToList();
    }

    public IReadOnlyList<(string Object, string Error)> Failures => _failures;

    public override string Message
    {
        get
        {
            var reported = _failures.Take(MaxReported).Select(f => $"  {f.Object}: {f.Error}");
            var remaining = _failures.Count - MaxReported;

            return $"Could not script {_failures.Count} object(s) of schema {_schema}. " +
                   "The generated script is incomplete and must not be deployed as it is:" +
                   Environment.NewLine +
                   string.Join(Environment.NewLine, reported) +
                   (remaining > 0 ? $"{Environment.NewLine}  ... and {remaining} more" : string.Empty);
        }
    }
}
