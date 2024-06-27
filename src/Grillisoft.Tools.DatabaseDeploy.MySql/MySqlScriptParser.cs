using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using System.Text;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.MySql;

public class MySqlScriptParser : IScriptParser
{
    public async IAsyncEnumerable<string> Parse(IFileInfo scriptFile, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var delimiter = ";";
        var buffer = new StringBuilder();

        await foreach (var line in scriptFile.EnumerateLinesAsync(cancellationToken))
        {
            if (GetDelimiter(line, ref delimiter))
                continue;

            if (!line.Trim().EndsWith(delimiter))
            {
                AppendSql(buffer, line);
                continue;
            }

            AppendSql(buffer, RemoveDelimiter(line, delimiter));
            if(buffer.Length > 0)
                yield return buffer.ToString();
            buffer.Clear();
        }
        
        if(buffer.Length > 0)
            yield return buffer.ToString();
    }

    private static void AppendSql(StringBuilder buffer, string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        buffer.AppendLine(line);
    }

    private static string RemoveDelimiter(string line, string delimiter)
    {
        line = line.Trim();
        return line.Substring(0, line.Length - delimiter.Length);
    }

    private static bool GetDelimiter(string line, ref string delimiter)
    {
        line = line.Trim();
        
        if (!line.StartsWith("DELIMITER", StringComparison.InvariantCultureIgnoreCase))
            return false;    

        delimiter = line.Replace("DELIMITER", "", StringComparison.OrdinalIgnoreCase).Trim();
        return true;
    }
}