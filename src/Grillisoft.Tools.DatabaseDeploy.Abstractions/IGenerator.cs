using System.IO.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.Abstractions;

public interface IGenerator
{
    Task GenerateRollback(IFileInfo deployFile, IFileInfo rollbackFile, string dialect, CancellationToken cancellationToken);
}