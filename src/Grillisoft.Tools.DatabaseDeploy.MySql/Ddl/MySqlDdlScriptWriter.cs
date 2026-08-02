using Grillisoft.Tools.DatabaseDeploy.Database.Ddl;

namespace Grillisoft.Tools.DatabaseDeploy.MySql.Ddl;

/// <summary>
/// Writes a generated MySQL script, announcing a delimiter of its own around any statement that
/// would otherwise be cut in half.
/// </summary>
/// <remarks>
/// <see cref="MySqlScriptParser"/> ends a statement at a line whose trimmed text ends with the
/// current delimiter, so a routine body - which holds its own semicolons, one per line - has to be
/// wrapped the way a hand written script wraps it. That is what the <c>DELIMITER</c> lines do, and
/// the parser consumes them rather than sending them to the server.
/// </remarks>
internal sealed class MySqlDdlScriptWriter(StreamWriter writer) : DdlScriptWriter(writer, "-- ", ";")
{
    /// <summary>
    /// Tried in order; the first one that does not end a line of the statement wins.
    /// </summary>
    private static readonly string[] Candidates = ["$$", "//", ";;"];

    public async override Task WriteStatement(string statement)
    {
        var trimmed = statement.Trim();

        if (ChooseDelimiter(trimmed) is not { } delimiter)
        {
            await base.WriteStatement(trimmed);
            return;
        }

        await Writer.WriteLineAsync("DELIMITER " + delimiter);
        await Writer.WriteLineAsync(trimmed);
        await Writer.WriteLineAsync(delimiter);
        await Writer.WriteLineAsync("DELIMITER ;");
        await Writer.WriteLineAsync();
    }

    /// <summary>
    /// The delimiter this statement needs, or <c>null</c> when a plain semicolon will do.
    /// </summary>
    /// <remarks>
    /// The test is the parser's own splitting rule, not a guess about which object types have a
    /// body: any line but the last ending with the delimiter would end the statement early.
    /// </remarks>
    internal static string? ChooseDelimiter(string statement)
    {
        var lines = statement.Split('\n')
                             .Select(line => line.TrimEnd('\r').TrimEnd())
                             .ToArray();

        // Only a line before the last can cut the statement short with a semicolon; the last one
        // ending with it is harmless, since that is where the statement ends anyway.
        if (!lines.Take(lines.Length - 1).Any(line => line.EndsWith(';')))
            return null;

        // A custom delimiter, though, is written on the line after the statement, so any line of
        // the statement ending with it - the last one included - would end it early.
        return Array.Find(Candidates,
                   candidate => !Array.Exists(lines, line => line.EndsWith(candidate, StringComparison.Ordinal)))
               ?? Candidates[0];
    }
}
