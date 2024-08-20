using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using System.Text;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle;

public class OracleScriptParser : IScriptParser
{
    public async IAsyncEnumerable<string> Parse(IFileInfo scriptFile, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var lines = await scriptFile.ReadAllLinesAsync(cancellationToken);
        var buffer = new StringBuilder();
        var sqlTerminator = DetectSqlTerminator(lines);

        foreach (var line in lines)
        {
            var trim = line.Trim();

            if (!(buffer.Length <= 0 && CanIgnore(trim)))
                buffer.AppendLine(line);

            if (trim.EndsWith(sqlTerminator))
            {
                var command = CleanSql(buffer.ToString());
                if (!string.IsNullOrWhiteSpace(command))
                    yield return command;

                buffer.Clear();
            }
        }

        if (buffer.Length > 0)
            yield return buffer.ToString();
    }

    private char DetectSqlTerminator(IEnumerable<string> lines)
    {
        if (lines.Any(line => line.Trim().StartsWith('/')))
        {
            return '/';
        }

        return ';';
    }

    private static readonly char[] TrimChars = ['\t', '\n', '\r', ' ', ';', '/'];

    private string CleanSql(string input)
    {
        return input.Trim(TrimChars);
    }

    private bool CanIgnore(string trim)
    {
        return string.IsNullOrEmpty(trim)
               || trim.StartsWith("rem", StringComparison.InvariantCultureIgnoreCase)
               || trim.StartsWith("set", StringComparison.InvariantCultureIgnoreCase)
               || trim.StartsWith("prompt", StringComparison.InvariantCultureIgnoreCase);
    }
}