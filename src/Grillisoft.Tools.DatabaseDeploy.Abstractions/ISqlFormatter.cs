using System.IO.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.Abstractions;

public interface ISqlFormatter
{
    Task FormatSql(IFileInfo sqlFile, CancellationToken cancellationToken);
}