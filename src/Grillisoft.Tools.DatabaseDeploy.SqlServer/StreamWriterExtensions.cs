namespace Grillisoft.Tools.DatabaseDeploy.SqlServer;

/// <summary>
/// Writing the pieces of a generated T-SQL script.
/// </summary>
internal static class StreamWriterExtensions
{
    /// <summary>
    /// A line holding nothing but this ends a batch, which is how sqlcmd, SQL Server Management
    /// Studio and <see cref="SqlServerScriptParser"/> all read a script.
    /// </summary>
    private const string BatchTerminator = "GO";

    private const string CommentPrefix = "-- ";

    /// <summary>
    /// Writes a comment as one <c>--</c> line per line of text, so that a comment holding a line
    /// break cannot turn the rest of itself into a statement.
    /// </summary>
    public static async Task WriteComment(this StreamWriter writer, string comment)
    {
        foreach (var line in comment.Split('\n'))
            await writer.WriteLineAsync(CommentPrefix + line.TrimEnd('\r'));
    }

    /// <summary>Writes a batch, its terminator, and the blank line that separates it from the next.</summary>
    public static async Task WriteStatement(this StreamWriter writer, string statement)
    {
        await writer.WriteLineAsync(statement.Trim());
        await writer.WriteLineAsync(BatchTerminator);
        await writer.WriteLineAsync();
    }
}
