namespace Grillisoft.Tools.DatabaseDeploy.Abstractions;

public interface IExecutable
{
    public Task<int> Execute(CancellationToken cancellationToken);
}