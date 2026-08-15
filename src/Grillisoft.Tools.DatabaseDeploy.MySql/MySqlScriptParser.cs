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
            if (HasSql(buffer))
                yield return buffer.ToString();
            buffer.Clear();
        }

        if (HasSql(buffer))
            yield return buffer.ToString();
    }

    /// <summary>
    /// Whether the buffer holds anything the server would accept.
    /// </summary>
    /// <remarks>
    /// A comment line does not end with the delimiter, so it stays in the buffer and comes out
    /// attached to the statement that follows it - except at the end of the file, where there is no
    /// such statement and the buffer would be flushed as a batch of nothing but comments. MySQL
    /// answers that with <c>ER_EMPTY_QUERY</c>, so it never leaves the parser.
    /// </remarks>
    private static bool HasSql(StringBuilder buffer)
    {
        if (buffer.Length == 0)
            return false;

        foreach (var line in buffer.ToString().Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0 && !IsComment(trimmed))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The two comment forms that run to the end of the line. <c>--</c> only opens a comment when
    /// followed by whitespace, which is why the bare <c>--</c> operator is not one.
    /// </summary>
    private static bool IsComment(string trimmedLine)
    {
        if (trimmedLine.StartsWith('#'))
            return true;

        return trimmedLine.StartsWith("--", StringComparison.Ordinal)
            && (trimmedLine.Length == 2 || char.IsWhiteSpace(trimmedLine[2]));
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