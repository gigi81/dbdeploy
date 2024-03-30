using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle;

public class OracleScriptParser : IScriptParser
{
    public async IAsyncEnumerable<string> Parse(IFileInfo scriptFile, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return await scriptFile.FileSystem.File.ReadAllTextAsync(scriptFile.FullName, cancellationToken);
    }
}