using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using System.Text;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle;

public class OracleScriptParser : IScriptParser
{
    public async IAsyncEnumerable<string> Parse(IFileInfo scriptFile, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var file = scriptFile.OpenRead();
        using var stream = new StreamReader(file);
        
        var line = await stream.ReadLineAsync(cancellationToken);
        var buffer = new StringBuilder();

        while (line != null)
        {
            if (!CanIgnore(line))
            {
                buffer.AppendLine(line);
                if (line.Trim().EndsWith(";")) //TODO: improve this
                {
                    yield return buffer.ToString();
                    buffer.Clear();
                }
            }
            
            line = await stream.ReadLineAsync(cancellationToken);
        }

        if (buffer.Length > 0)
            yield return buffer.ToString();
    }

    private bool CanIgnore(string line)
    {
        var trim = line.Trim();

        return trim.StartsWith("rem", StringComparison.InvariantCultureIgnoreCase)
               || trim.StartsWith("prompt", StringComparison.InvariantCultureIgnoreCase);
    }
}