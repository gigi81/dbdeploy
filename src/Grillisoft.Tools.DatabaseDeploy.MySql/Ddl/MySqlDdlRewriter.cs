using System.Text.RegularExpressions;

namespace Grillisoft.Tools.DatabaseDeploy.MySql.Ddl;

/// <summary>
/// Turns what <c>SHOW CREATE ...</c> returns into something that can be replayed somewhere else.
/// </summary>
/// <remarks>
/// The server answers with the DDL of the object <em>as it stands on this database</em>, which is
/// not the same thing as DDL that recreates it elsewhere: it carries the user that defined it, the
/// counter an auto increment column has reached, and - for a view - the name of the database it was
/// read from. All three have to go, or the script only replays where it was taken from.
/// <para>
/// Everything here is a pure string transformation over machine generated text, which is what makes
/// it testable without a server.
/// </para>
/// </remarks>
internal static partial class MySqlDdlRewriter
{
    /// <summary>
    /// <c>DEFINER=`user`@`host`</c>, in all the spellings the server emits: either half can be
    /// backtick quoted, single quoted or bare, and <c>CURRENT_USER</c> appears with and without
    /// parentheses.
    /// </summary>
    [GeneratedRegex(
        @"\s*DEFINER\s*=\s*(?:CURRENT_USER(?:\s*\(\s*\))?|(?:`(?:[^`]|``)*`|'(?:[^']|'')*'|[^\s@]+)\s*@\s*(?:`(?:[^`]|``)*`|'(?:[^']|'')*'|\S+))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DefinerClause();

    /// <summary>
    /// The table option, not the column keyword: the column form is a bare <c>AUTO_INCREMENT</c>
    /// with no <c>=</c>, so matching the assignment cannot touch it.
    /// </summary>
    [GeneratedRegex(@"\s*AUTO_INCREMENT\s*=\s*\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AutoIncrementOption();

    /// <summary>A <c>CONSTRAINT `x` FOREIGN KEY ...</c> definition line inside a CREATE TABLE.</summary>
    [GeneratedRegex(@"^\s*CONSTRAINT\s+(`(?:[^`]|``)*`)\s+FOREIGN\s+KEY\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ForeignKeyDefinition();

    /// <summary>
    /// Drops the definer of a view, routine, trigger or event.
    /// </summary>
    /// <remarks>
    /// <c>SQL SECURITY DEFINER</c> is deliberately left alone: with no <c>DEFINER=</c> in front of
    /// it the server defaults the definer to whoever replays the script, which is precisely what
    /// makes it portable. Rewriting it to <c>INVOKER</c> would change what the object does.
    /// </remarks>
    public static string StripDefiner(string ddl) => DefinerClause().Replace(ddl, string.Empty);

    /// <summary>
    /// Drops the auto increment counter a table has reached, which is data rather than schema and
    /// makes the script differ on every run.
    /// </summary>
    public static string StripAutoIncrement(string ddl) => AutoIncrementOption().Replace(ddl, string.Empty);

    /// <summary>
    /// Drops the database qualifier the server bakes into what it returns.
    /// </summary>
    /// <remarks>
    /// This is the one that decides whether a script is portable at all: MySQL stores a view's
    /// definition fully qualified, so <c>SHOW CREATE VIEW</c> on <c>northwind</c> comes back
    /// selecting from <c>`northwind`.`orders`</c>, and replaying that into <c>northwind_test</c>
    /// either fails or - worse - silently points the new view at the old database.
    /// </remarks>
    public static string RemoveDatabaseQualifier(string ddl, string databaseName)
    {
        if (string.IsNullOrEmpty(databaseName))
            return ddl;

        return ddl.Replace($"{databaseName.Quote()}.", string.Empty, StringComparison.Ordinal)
                  .Replace($"{databaseName}.", string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// Takes the inline foreign keys out of a <c>CREATE TABLE</c> and returns them as
    /// <c>ALTER TABLE ... ADD CONSTRAINT</c> statements of their own.
    /// </summary>
    /// <remarks>
    /// Inline foreign keys make the order of the <c>CREATE TABLE</c> statements a constraint, and
    /// two tables referencing each other then have no valid order at all. Split out, the tables can
    /// be written in any order and the keys land after all of them - which is what Oracle and SQL
    /// Server already do, through their scripter's options rather than by hand.
    /// <para>
    /// The <c>KEY</c> line backing each foreign key is deliberately left in the table: the server
    /// emits it separately, and keeping it means the later <c>ADD CONSTRAINT</c> reuses that index
    /// instead of creating a second one. Inline <c>CHECK</c> constraints stay too, since MySQL does
    /// not allow one to reference another table.
    /// </para>
    /// </remarks>
    /// <param name="table">The name of the table, unquoted.</param>
    public static (string Table, IReadOnlyList<(string Name, string Statement)> ForeignKeys) SplitForeignKeys(
        string createTable,
        string table)
    {
        var lines = createTable.Split('\n');
        var kept = new List<string>(lines.Length);
        var foreignKeys = new List<(string Name, string Statement)>();

        foreach (var line in lines)
        {
            var match = ForeignKeyDefinition().Match(line);
            if (!match.Success)
            {
                kept.Add(line);
                continue;
            }

            var definition = line.TrimEnd('\r').Trim().TrimEnd(',');
            foreignKeys.Add((
                match.Groups[1].Value.Trim('`').Replace("``", "`", StringComparison.Ordinal),
                $"ALTER TABLE {table.Quote()} ADD {definition}"));
        }

        return (foreignKeys.Count == 0 ? createTable : RepairTrailingComma(kept), foreignKeys);
    }

    /// <summary>
    /// Takes off the comma the removed lines left behind. Whatever definition is now last must not
    /// end with one, or the statement no longer parses - and if every definition was a foreign key,
    /// the line before was the last column and needs the same treatment.
    /// </summary>
    /// <remarks>
    /// The comma is cut out of the line rather than trimmed off the end of it, so that a line
    /// ending in a carriage return keeps it: the lines around it still have theirs, and a statement
    /// with one line ending in the middle of it is a nasty thing to find in a diff.
    /// </remarks>
    private static string RepairTrailingComma(List<string> lines)
    {
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var trimmed = lines[i].Trim();

            if (trimmed.Length == 0)
                continue;

            // The closing ") ENGINE=..." and anything after it is not a definition.
            if (trimmed.StartsWith(')'))
                continue;

            if (trimmed.EndsWith(','))
                lines[i] = lines[i].Remove(lines[i].LastIndexOf(','), 1);

            break;
        }

        return string.Join('\n', lines);
    }
}
