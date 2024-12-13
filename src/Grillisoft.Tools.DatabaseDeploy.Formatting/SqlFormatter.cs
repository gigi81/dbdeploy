using System.IO.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.Formatting;

public class SqlFormatter : ISqlFormatter
{
    public Task FormatSql(IFileInfo sqlFile, CancellationToken cancellationToken)
    {
        //TODO: implement
        return Task.CompletedTask;
    }
}
