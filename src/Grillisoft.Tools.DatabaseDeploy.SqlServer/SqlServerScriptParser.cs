using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using System.Text;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer;

internal class SqlServerScriptParser : IScriptParser
{
    public async IAsyncEnumerable<string> Parse(IFileInfo scriptFile, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var file = scriptFile.OpenRead();
        using var stream = new StreamReader(file);
        
        var line = await stream.ReadLineAsync(cancellationToken);
        var buffer = new StringBuilder();

        while (line != null)
        {
            if (line.Trim().Equals("GO", StringComparison.InvariantCultureIgnoreCase))
            {
                yield return buffer.ToString();
                buffer.Clear();
            }
            else
            {
                buffer.AppendLine(line);
            }
            
            line = await stream.ReadLineAsync(cancellationToken);
        }
    }
}