using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.PostgreSql;

public class PostgreSqlScriptParser : IScriptParser
{
    public async IAsyncEnumerable<string> Parse(IFileInfo scriptFile, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return await scriptFile.ReadAllTextAsync(cancellationToken);
    }
}