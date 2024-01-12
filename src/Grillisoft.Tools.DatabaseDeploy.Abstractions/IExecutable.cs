namespace Grillisoft.Tools.DatabaseDeploy.Abstractions;

public interface IExecutable
{
    public Task Execute(CancellationToken cancellationToken);
}