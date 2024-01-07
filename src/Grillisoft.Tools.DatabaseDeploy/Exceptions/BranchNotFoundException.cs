namespace Grillisoft.Tools.DatabaseDeploy.Exceptions;

[Serializable]
public class BranchNotFoundException : Exception
{
    public BranchNotFoundException()
    {
    }
    public BranchNotFoundException(string message) : base(message) { }
    public BranchNotFoundException(string message, Exception inner) : base(message, inner) { }
}