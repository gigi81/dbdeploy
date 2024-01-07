using System.IO.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer;

internal class SqlServerScriptParser : IScriptParser
{
    public Task<IAsyncEnumerable<string>> Parse(IFileInfo scriptFile, CancellationToken cancellationToken)
    {
        using (var file = scriptFile.OpenRead())
        {
            throw new NotImplementedException();
        }
    }
}