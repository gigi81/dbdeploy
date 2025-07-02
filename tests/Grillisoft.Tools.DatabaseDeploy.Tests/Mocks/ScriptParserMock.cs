using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.Tests.Mocks;

public class ScriptParserMock : IScriptParser
{
    public async IAsyncEnumerable<string> Parse(IFileInfo scriptFile, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return await scriptFile.FileSystem.File.ReadAllTextAsync(scriptFile.FullName, cancellationToken);
    }
}