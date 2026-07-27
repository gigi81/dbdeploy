using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle;

public partial class OracleScriptParser : IScriptParser
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

            // A line holding nothing but the terminator always closes the statement, the way
            // SQL*Plus does. Quote counting cannot be trusted on its own: a single apostrophe in a
            // PL/SQL comment leaves it convinced the statement is still inside a string literal,
            // and every following statement gets swallowed into the same command.
            var isTerminatorLine = trim.Length == 1 && trim[0] == sqlTerminator;

            if (isTerminatorLine || (count % 2 == 0 && trim.EndsWith(sqlTerminator)))
            {
                var command = CleanSql(buffer.ToString(), sqlTerminator);
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

    private static readonly char[] Whitespace = ['\t', '\n', '\r', ' '];

    /// <summary>
    /// Matches the END of a PL/SQL unit, whose semicolon belongs to the statement and must survive.
    /// </summary>
    [GeneratedRegex(@"\bEND\s*(""?[A-Za-z0-9_$#]+""?)?\s*;$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PlSqlEnd();

    private static string CleanSql(string input, char sqlTerminator)
    {
        var sql = input.Trim(Whitespace).TrimEnd(sqlTerminator).Trim(Whitespace);

        // Stripping the trailing semicolon off a program unit turns its END; into an END and the
        // server rejects the whole body.
        return PlSqlEnd().IsMatch(sql) ? sql : sql.TrimEnd(';').Trim(Whitespace);
    }

    private static bool CanIgnore(string trim)
    {
        return string.IsNullOrEmpty(trim)
               || trim.StartsWith("rem", StringComparison.InvariantCultureIgnoreCase)
               || trim.StartsWith("set", StringComparison.InvariantCultureIgnoreCase)
               || trim.StartsWith("prompt", StringComparison.InvariantCultureIgnoreCase);
    }
}