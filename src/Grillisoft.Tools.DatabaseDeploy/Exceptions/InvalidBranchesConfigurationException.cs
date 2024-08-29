namespace Grillisoft.Tools.DatabaseDeploy.Exceptions;

public class InvalidBranchesConfigurationException : Exception
{
    private readonly IReadOnlyCollection<string> _errors;

    public InvalidBranchesConfigurationException(IReadOnlyCollection<string> errors)
    {
        _errors = errors;
    }

    public IReadOnlyCollection<string> Errors => _errors;
    
    public override string Message => $"Invalid branches configuration: {string.Join(", ", _errors)}";
}