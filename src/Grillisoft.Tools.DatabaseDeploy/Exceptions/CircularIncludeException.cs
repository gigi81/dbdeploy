namespace Grillisoft.Tools.DatabaseDeploy.Exceptions;

public class CircularIncludeException : Exception
{
    private readonly string _filename;

    public CircularIncludeException(string filename)
    {
        _filename = filename;
    }

    public override string Message => $"Circular include detected on file {_filename}";
}