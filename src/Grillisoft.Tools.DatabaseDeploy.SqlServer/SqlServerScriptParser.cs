using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using System.Text;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer;

/// <summary>
/// Splits a script into batches the way sqlcmd does: a line holding nothing but <c>GO</c> ends the
/// batch.
/// </summary>
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
                if (HasContent(buffer))
                    yield return buffer.ToString();

                buffer.Clear();
            }
            else
            {
                buffer.AppendLine(line);
            }

            line = await stream.ReadLineAsync(cancellationToken);
        }

        if (HasContent(buffer))
            yield return buffer.ToString();
    }

    /// <summary>
    /// An empty batch - two terminators in a row, or a trailing one at the end of the file - is not
    /// something the server will accept, so it never leaves the parser.
    /// </summary>
    private static bool HasContent(StringBuilder buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            if (!char.IsWhiteSpace(buffer[i]))
                return true;
        }

        return false;
    }
}
