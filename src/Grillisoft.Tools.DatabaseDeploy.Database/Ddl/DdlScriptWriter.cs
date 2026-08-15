namespace Grillisoft.Tools.DatabaseDeploy.Database.Ddl;

/// <summary>
/// Writes the pieces of a generated script in the shape the dialect's own script parser reads back.
/// </summary>
/// <remarks>
/// The two things that differ between dialects are how a comment opens and what ends a statement:
/// SQL Server uses <c>--</c> and a line holding <c>GO</c>, Oracle <c>REM</c> and a line holding
/// <c>/</c>, MySQL and PostgreSQL <c>--</c> and <c>;</c>. Everything else about writing a script is
/// the same everywhere, so it lives here once.
/// </remarks>
public class DdlScriptWriter
{
    private readonly string _commentPrefix;
    private readonly string _statementTerminator;

    /// <param name="commentPrefix">Opens a comment line, trailing space included.</param>
    /// <param name="statementTerminator">
    /// Written on a line of its own after every statement. A line holding nothing but this is what
    /// the dialect's <c>IScriptParser</c> splits on.
    /// </param>
    public DdlScriptWriter(StreamWriter writer, string commentPrefix, string statementTerminator)
    {
        Writer = writer;
        _commentPrefix = commentPrefix;
        _statementTerminator = statementTerminator;
    }

    /// <summary>The underlying writer, for the rare statement a subclass has to lay out itself.</summary>
    protected StreamWriter Writer { get; }

    /// <summary>
    /// Writes a comment as one prefixed line per line of text, so that a comment holding a line
    /// break cannot turn the rest of itself into a statement.
    /// </summary>
    public async Task WriteComment(string comment)
    {
        foreach (var line in comment.Split('\n'))
            await Writer.WriteLineAsync(_commentPrefix + line.TrimEnd('\r'));
    }

    public Task WriteLine() => Writer.WriteLineAsync();

    /// <summary>Writes a statement, its terminator, and the blank line that separates it from the next.</summary>
    public async virtual Task WriteStatement(string statement)
    {
        await Writer.WriteLineAsync(statement.Trim());
        await Writer.WriteLineAsync(_statementTerminator);
        await Writer.WriteLineAsync();
    }
}
