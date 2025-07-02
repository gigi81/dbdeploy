using System.IO.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.Abstractions;

public interface IScriptParser
{
    IAsyncEnumerable<string> Parse(IFileInfo scriptFile, CancellationToken cancellationToken);
}
