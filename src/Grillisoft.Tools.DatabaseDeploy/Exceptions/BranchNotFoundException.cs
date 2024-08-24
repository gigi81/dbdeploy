namespace Grillisoft.Tools.DatabaseDeploy.Exceptions;

public class BranchNotFoundException : Exception
{
    private readonly string _branchName;

    public BranchNotFoundException(string branchName)
    {
        _branchName = branchName;
    }

    public string BranchName => _branchName;

    public override string Message => $"Branch {_branchName} not found";
}