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
        var count = 0;

        foreach (var line in lines)
        {
            var trim = line.Trim();

            if (!(buffer.Length <= 0 && CanIgnore(trim)))
                buffer.AppendLine(line);

            count += trim.Count(c => c == '\'');

            if (count % 2 == 0 && trim.EndsWith(sqlTerminator))
            {
                var command = CleanSql(buffer.ToString());
                if (!string.IsNullOrWhiteSpace(command))
                    yield return command;

                buffer.Clear();
                count = 0;
            }
        }

        if (buffer.Length > 0)
            yield return buffer.ToString();
    }

    private static char DetectSqlTerminator(IEnumerable<string> lines)
    {
        if (lines.Any(line => line.Trim().StartsWith('/')))
        {
            return '/';
        }

        return ';';
    }

    private static readonly char[] TrimChars = ['\t', '\n', '\r', ' ', ';', '/'];

    private static string CleanSql(string input)
    {
        return input.Trim(TrimChars);
    }

    private static bool CanIgnore(string trim)
    {
        return string.IsNullOrEmpty(trim)
               || trim.StartsWith("rem", StringComparison.InvariantCultureIgnoreCase)
               || trim.StartsWith("set", StringComparison.InvariantCultureIgnoreCase)
               || trim.StartsWith("prompt", StringComparison.InvariantCultureIgnoreCase);
    }
}