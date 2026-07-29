using System.Text;
using System.Text.RegularExpressions;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle.Ddl;

/// <summary>
/// Splits the CLOB returned by <c>DBMS_METADATA.GET_DDL</c> into individual statements.
/// </summary>
/// <remarks>
/// One call to <c>GET_DDL</c> usually yields one statement, but not always: a table can come back as
/// a <c>CREATE TABLE</c> followed by <c>ALTER TABLE ... ADD CONSTRAINT</c> or by a
/// <c>CREATE UNIQUE INDEX</c> for constraints Oracle refuses to inline. Each of those has to be sent
/// to the server on its own.
/// <para>
/// A statement boundary is only recognised at the beginning of a line, and only when that position
/// is not inside a string literal, a quoted identifier or a comment, so a <c>CREATE</c> sitting
/// inside a check constraint expression or a column default is not mistaken for one.
/// PL/SQL sources are never split: they contain statement keywords at the start of a line all the
/// time and are a single statement anyway.
/// </para>
/// </remarks>
internal static partial class OracleDdlSplitter
{
    [GeneratedRegex(@"^(CREATE|ALTER|COMMENT|GRANT|REVOKE|DROP|INSERT)\s",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StatementStart();

    [GeneratedRegex(@"^ALTER\s+[A-Z]+\s+""?[A-Za-z0-9_$#]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TrailingAlter();

    private static readonly char[] TrimmedFromSql = ['\r', '\n', '\t', ' ', ';', '/'];
    private static readonly char[] TrimmedFromPlSql = ['\r', '\n', '\t', ' ', '/'];

    /// <param name="ddl">Raw DDL as returned by Oracle.</param>
    /// <param name="isPlSql">
    /// True for stored program units, whose body is a single statement that must be kept intact
    /// together with its trailing <c>END;</c>.
    /// </param>
    public static IReadOnlyList<string> Split(string? ddl, bool isPlSql)
    {
        if (string.IsNullOrWhiteSpace(ddl))
            return [];

        if (isPlSql)
            return SplitPlSql(ddl.Trim(TrimmedFromPlSql));

        var statements = new List<string>();
        var current = new StringBuilder();
        var scanner = new Scanner();

        foreach (var line in ddl.Split('\n'))
        {
            if (scanner.AtStatementBoundary && current.Length > 0 && StatementStart().IsMatch(line.TrimStart()))
                Flush(statements, current);

            current.Append(line.TrimEnd('\r')).Append('\n');
            scanner.Advance(line);
        }

        Flush(statements, current);
        return statements;
    }

    /// <summary>
    /// The body of a program unit is one statement, but <c>DBMS_METADATA</c> tacks an
    /// <c>ALTER TRIGGER … ENABLE</c> onto the end of a trigger, and the server rejects the two when
    /// they arrive together. Only trailing <c>ALTER</c> lines are peeled off: the body itself always
    /// ends with its <c>END;</c>, so nothing inside it can be mistaken for one.
    /// </summary>
    private static IReadOnlyList<string> SplitPlSql(string ddl)
    {
        if (string.IsNullOrWhiteSpace(ddl))
            return [];

        var lines = ddl.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        var trailing = new List<string>();

        while (lines.Count > 1 && TrailingAlter().IsMatch(lines[^1].TrimStart()))
        {
            trailing.Insert(0, lines[^1].Trim(TrimmedFromSql));
            lines.RemoveAt(lines.Count - 1);
        }

        var body = string.Join('\n', lines).Trim(TrimmedFromPlSql);
        var statements = new List<string>();

        if (!string.IsNullOrWhiteSpace(body))
            statements.Add(body);

        statements.AddRange(trailing.Where(t => !string.IsNullOrWhiteSpace(t)));
        return statements;
    }

    private static void Flush(List<string> statements, StringBuilder current)
    {
        var statement = current.ToString().Trim(TrimmedFromSql);
        current.Clear();

        if (!string.IsNullOrWhiteSpace(statement))
            statements.Add(statement);
    }

    /// <summary>
    /// Tracks whether the text carried over from the previous lines leaves us inside a literal or a
    /// block comment, in which case the next line cannot be starting a new statement.
    /// </summary>
    private struct Scanner
    {
        private bool _inString;
        private bool _inQuotedIdentifier;
        private bool _inBlockComment;

        public readonly bool AtStatementBoundary => !_inString && !_inQuotedIdentifier && !_inBlockComment;

        public void Advance(string line)
        {
            var i = 0;

            while (i < line.Length)
            {
                var c = line[i];
                var next = i + 1 < line.Length ? line[i + 1] : '\0';
                i++;

                if (_inBlockComment)
                {
                    if (c == '*' && next == '/')
                    {
                        _inBlockComment = false;
                        i++;
                    }

                    continue;
                }

                if (_inString)
                {
                    if (c != '\'')
                        continue;

                    if (next == '\'')
                        i++; // '' is an escaped quote, not the end of the literal
                    else
                        _inString = false;

                    continue;
                }

                if (_inQuotedIdentifier)
                {
                    if (c == '"')
                        _inQuotedIdentifier = false;

                    continue;
                }

                if (c == '-' && next == '-')
                    return; // line comment: nothing else on this line can change the state

                if (c == '/' && next == '*')
                {
                    _inBlockComment = true;
                    i++;
                }
                else if (c == '\'')
                {
                    _inString = true;
                }
                else if (c == '"')
                {
                    _inQuotedIdentifier = true;
                }
            }
        }
    }
}
