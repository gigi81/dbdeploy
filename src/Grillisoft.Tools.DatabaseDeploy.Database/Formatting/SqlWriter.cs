using System.Text;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.Database.Formatting;

/// <summary>
/// The text the layout is written to: an indented buffer where whitespace is queued rather than
/// written. Asking for a space or a line break only records the intention, and the next
/// <see cref="Write"/> decides what survives - so a space in front of a line break disappears, and
/// several line breaks in a row collapse into the longest one asked for. That is what lets
/// <see cref="SqlEmitter"/> announce what it wants around a token without knowing what the token
/// after it will want.
/// </summary>
internal sealed class SqlWriter
{
    private readonly SqlFormatterOptions _options;
    private readonly StringBuilder _output = new();

    private int _pendingNewlines;
    private bool _pendingSpace;

    public SqlWriter(SqlFormatterOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// How many levels the next line starts at. Changing it takes effect at the next line break,
    /// never in the middle of a line.
    /// </summary>
    public int Indent { get; set; }

    /// <summary>The text <see cref="Indent"/> is written as, for the callers laying out their own.</summary>
    public string IndentText => IndentTextFor(this.Indent);

    /// <summary>
    /// How far into the line the next write lands, counted in characters. With a line break queued
    /// that is the indent the line will start at rather than zero.
    /// </summary>
    public int Column
    {
        get
        {
            if (_pendingNewlines > 0)
                return this.IndentText.Length;

            for (var i = _output.Length - 1; i >= 0; i--)
            {
                if (_output[i] == '\n')
                    return _output.Length - i - 1;
            }

            return _output.Length;
        }
    }

    public void Write(string text)
    {
        if (text.Length == 0)
            return;

        if (_pendingNewlines > 0)
        {
            //nothing above to be separated from, so a script never opens with a blank line
            if (_output.Length > 0)
            {
                for (var i = 0; i < _pendingNewlines; i++)
                    _output.Append(_options.NewLine);
            }

            _pendingNewlines = 0;
            _pendingSpace = false;
            _output.Append(this.IndentText);
        }
        else if (_pendingSpace && _output.Length > 0)
        {
            _output.Append(' ');
        }

        _pendingSpace = false;
        _output.Append(text);
    }

    /// <summary>
    /// Writes closed up against what came before, dropping any space that was queued: what a comma,
    /// a closing parenthesis or a member access operator needs.
    /// </summary>
    public void WriteTight(string text)
    {
        SuppressSpace();
        Write(text);
    }

    /// <summary>
    /// Writes text that may span several lines: the first line goes where the cursor already is,
    /// and every line after it is put under the current <see cref="Indent"/>. The text keeps
    /// whatever line endings it was written with rather than taking the ones the rest of the output
    /// uses - a block comment carried over from the source is still the author's text.
    /// </summary>
    public void WriteIndented(string text)
    {
        var lines = text.Split('\n');

        if (lines.Length == 1)
        {
            Write(text);
            return;
        }

        var builder = new StringBuilder(text.Length);
        var indent = this.IndentText;

        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
                builder.Append('\n');

            var line = lines[i];
            var carriageReturn = line.EndsWith('\r');
            if (carriageReturn)
                line = line[..^1];

            if (i > 0)
            {
                line = _options.TrimTrailingWhitespace ? line.Trim() : line.TrimStart();
                line = indent + line;
            }

            builder.Append(line);
            if (carriageReturn)
                builder.Append('\r');
        }

        Write(builder.ToString());
    }

    /// <summary>Drops a queued space without writing anything.</summary>
    public void SuppressSpace() => _pendingSpace = false;

    public void Space() => _pendingSpace = true;

    public void NewLine()
    {
        _pendingNewlines = Math.Max(_pendingNewlines, 1);
        _pendingSpace = false;
    }

    /// <summary>
    /// Exactly one line break, discarding any blank line already queued.
    /// </summary>
    public void ForceNewLine()
    {
        _pendingNewlines = 1;
        _pendingSpace = false;
    }

    public void BlankLines(int count)
    {
        _pendingNewlines = Math.Max(_pendingNewlines, count + 1);
        _pendingSpace = false;
    }

    /// <summary>
    /// The finished text: whatever whitespace was queued at the end is dropped, and the final
    /// newline is added when the options ask for one and there is something to end.
    /// </summary>
    public string Finish()
    {
        var text = _output.ToString().TrimEnd();

        if (_options.InsertFinalNewline && text.Length > 0)
            text += _options.NewLine;

        return text;
    }

    private string IndentTextFor(int level) =>
        level <= 0 ? string.Empty : string.Concat(Enumerable.Repeat(_options.Indent, level));
}
