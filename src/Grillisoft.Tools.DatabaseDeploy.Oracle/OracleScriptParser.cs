using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle;

/// <summary>
/// Cuts an Oracle script into the statements the server is sent one at a time.
/// </summary>
/// <remarks>
/// A script is terminated one of two ways: SQL*Plus style, by a <c>/</c> on a line of its own,
/// which is the only way a PL/SQL unit can be ended since its body is full of semicolons; or, when
/// there is no such line, by a semicolon ending a line. Either way the terminator has to be
/// <em>code</em>: a block comment ends with a slash, and a comment or a literal can hold a
/// semicolon or an apostrophe anywhere, so every line goes through <see cref="OracleSqlScanner"/>.
/// </remarks>
public partial class OracleScriptParser : IScriptParser
{
    private static readonly char[] Whitespace = ['\t', '\n', '\r', ' '];

    public async IAsyncEnumerable<string> Parse(IFileInfo scriptFile, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var lines = await scriptFile.ReadAllLinesAsync(cancellationToken);
        var terminator = DetectSqlTerminator(lines);
        var buffer = new StringBuilder();
        var scanner = new OracleSqlScanner();
        var hasCode = false;

        foreach (var line in lines)
        {
            // Checked before scanning, so that "REM don't" cannot open a literal.
            if (buffer.Length == 0 && scanner.InCode && CanIgnore(line.Trim()))
                continue;

            var inBlockComment = scanner.InBlockComment;
            var (first, last) = scanner.Scan(line);

            // A line holding nothing but the terminator always closes the statement, the way
            // SQL*Plus does - even when the scanner believes it is inside a literal, so that one
            // unbalanced quote costs one statement rather than every statement after it. Inside a
            // block comment it is only part of the comment.
            if (!inBlockComment && IsOnlyTerminator(line, first, last, terminator))
            {
                if (TryGetCommand(buffer, hasCode, out var command))
                    yield return command;

                buffer.Clear();
                hasCode = false;
                scanner = default;
                continue;
            }

            hasCode |= first >= 0;

            if (scanner.InCode && last >= 0 && line[last] == terminator)
            {
                // The terminator comes off where it is, not off the end of the line, which can
                // carry a comment after it.
                buffer.AppendLine(line.Remove(last, 1));

                if (TryGetCommand(buffer, hasCode, out var command))
                    yield return command;

                buffer.Clear();
                hasCode = false;
                continue;
            }

            buffer.AppendLine(line);
        }

        if (TryGetCommand(buffer, hasCode, out var remaining))
            yield return remaining;
    }

    /// <summary>
    /// The script is terminated by <c>/</c> when any line is nothing but one: a line merely
    /// starting with a slash can be opening a block comment.
    /// </summary>
    private static char DetectSqlTerminator(IEnumerable<string> lines)
    {
        var scanner = new OracleSqlScanner();

        foreach (var line in lines)
        {
            if (scanner.InCode && CanIgnore(line.Trim()))
                continue;

            var inBlockComment = scanner.InBlockComment;
            var (first, last) = scanner.Scan(line);

            if (!inBlockComment && IsOnlyTerminator(line, first, last, '/'))
                return '/';
        }

        return ';';
    }

    private static bool IsOnlyTerminator(string line, int first, int last, char terminator)
        => first >= 0 && first == last && line[first] == terminator;

    /// <summary>
    /// The statement in <paramref name="buffer"/>, unless it is nothing but comments: the server
    /// answers one of those with ORA-00900.
    /// </summary>
    private static bool TryGetCommand(StringBuilder buffer, bool hasCode, out string command)
    {
        command = hasCode ? CleanSql(buffer.ToString()) : string.Empty;
        return !string.IsNullOrWhiteSpace(command);
    }

    /// <summary>
    /// Matches the END of a PL/SQL unit, whose semicolon belongs to the statement and must survive.
    /// </summary>
    [GeneratedRegex(@"\bEND\s*(""?[A-Za-z0-9_$#]+""?)?\s*;$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PlSqlEnd();

    /// <summary>
    /// A statement terminated by a <c>/</c> line can still end with the semicolon it would have
    /// had in a semicolon script, and the server rejects a SQL statement carrying one.
    /// </summary>
    private static string CleanSql(string input)
    {
        var sql = input.Trim(Whitespace);
        var last = OracleSqlScanner.LastCodeIndex(sql);

        if (last < 0 || sql[last] != ';')
            return sql;

        // Stripping the trailing semicolon off a program unit turns its END; into an END and the
        // server rejects the whole body.
        return PlSqlEnd().IsMatch(sql[..(last + 1)]) ? sql : sql.Remove(last, 1).Trim(Whitespace);
    }

    private static bool CanIgnore(string trim)
    {
        return string.IsNullOrEmpty(trim)
               || trim.StartsWith("rem", StringComparison.InvariantCultureIgnoreCase)
               || trim.StartsWith("set", StringComparison.InvariantCultureIgnoreCase)
               || trim.StartsWith("prompt", StringComparison.InvariantCultureIgnoreCase);
    }
}
