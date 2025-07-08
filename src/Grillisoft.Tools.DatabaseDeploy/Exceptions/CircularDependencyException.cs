namespace Grillisoft.Tools.DatabaseDeploy.Exceptions;

public class CircularDependencyException : Exception
{
    private readonly List<string> _names;

    public CircularDependencyException(IEnumerable<string> names)
    {
        _names = names.ToList();
    }

    public override string Message
        => $"Circular dependency detected: {string.Join(",", _names)}";
}