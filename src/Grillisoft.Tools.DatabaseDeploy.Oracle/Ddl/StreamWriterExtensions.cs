namespace Grillisoft.Tools.DatabaseDeploy.Oracle.Ddl;

/// <summary>
/// Writing the pieces of a generated PL/SQL script.
/// </summary>
internal static class StreamWriterExtensions
{
    /// <summary>
    /// A line holding nothing but this ends a statement, which is how SQL*Plus and
    /// <see cref="OracleScriptParser"/> both read a script.
    /// </summary>
    private const string StatementTerminator = "/";

    /// <summary>
    /// SQL*Plus <c>REM</c> rather than <c>--</c>, because <see cref="OracleScriptParser"/> drops
    /// those lines instead of sending them to the server as a statement of their own.
    /// </summary>
    private const string CommentPrefix = "REM ";

    /// <summary>
    /// Writes a comment as one <c>REM</c> line per line of text, so that a comment holding a line
    /// break cannot turn the rest of itself into a statement.
    /// </summary>
    public static async Task WriteComment(this StreamWriter writer, string comment)
    {
        foreach (var line in comment.Split('\n'))
            await writer.WriteLineAsync(CommentPrefix + line.TrimEnd('\r'));
    }

    /// <summary>Writes a statement, its terminator, and the blank line that separates it from the next.</summary>
    public static async Task WriteStatement(this StreamWriter writer, string statement)
    {
        await writer.WriteLineAsync(statement.Trim());
        await writer.WriteLineAsync(StatementTerminator);
        await writer.WriteLineAsync();
    }
}
