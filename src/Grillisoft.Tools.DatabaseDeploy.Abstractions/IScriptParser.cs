using System.IO.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.Abstractions;

public interface IScriptParser
{
    Task<IAsyncEnumerable<string>> Parse(IFileInfo scriptFile);
}
