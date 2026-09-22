namespace Grillisoft.Tools.DatabaseDeploy.Oracle;

/// <summary>
/// Tells the code of an Oracle script from its comments and literals, carrying over from one line
/// to the next whatever a line leaves open.
/// </summary>
/// <remarks>
/// Both ways a script is cut into statements depend on this: <see cref="OracleScriptParser"/> for
/// what is deployed, and <c>OracleDdlSplitter</c> for what <c>DBMS_METADATA</c> hands back. A
/// terminator only counts when it is code - a block comment ends with a slash, and a comment or a
/// literal can hold a semicolon or an apostrophe anywhere.
/// <para>
/// Understood: <c>'...'</c> literals with <c>''</c> escapes, <c>q'[...]'</c> literals and their
/// <c>nq'</c> form, <c>"..."</c> identifiers, <c>--</c> comments to the end of the line and
/// <c>/* ... */</c> comments across lines.
/// </para>
/// </remarks>
internal struct OracleSqlScanner
{
    private enum State
    {
        Code,
        LineComment,
        BlockComment,
        String,
        QuotedString,
        QuotedIdentifier,
    }

    private State _state;
    private char _quoteClose;

    /// <summary>Whether whatever came before leaves the scanner outside every comment and literal.</summary>
    public readonly bool InCode => _state == State.Code;

    public readonly bool InBlockComment => _state == State.BlockComment;

    /// <summary>
    /// Scans <paramref name="text"/>, which may span several lines, and returns where its code is:
    /// the first and the last character that is neither whitespace nor part of a comment, or -1 for
    /// both when there is none. The characters of a literal are code.
    /// </summary>
    public (int First, int Last) Scan(string text)
    {
        var first = -1;
        var last = -1;
        var i = 0;

        void Mark(int index)
        {
            if (char.IsWhiteSpace(text[index]))
                return;

            if (first < 0)
                first = index;

            last = index;
        }

        while (i < text.Length)
        {
            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';

            switch (_state)
            {
                case State.LineComment:
                    if (c == '\n')
                        _state = State.Code;

                    i++;
                    continue;

                case State.BlockComment:
                    if (c == '*' && next == '/')
                    {
                        _state = State.Code;
                        i += 2;
                        continue;
                    }

                    i++;
                    continue;

                case State.String:
                    Mark(i);

                    if (c == '\'' && next == '\'')
                    {
                        // '' is an escaped quote, not the end of the literal
                        Mark(i + 1);
                        i += 2;
                        continue;
                    }

                    if (c == '\'')
                        _state = State.Code;

                    i++;
                    continue;

                case State.QuotedString:
                    Mark(i);

                    if (c == _quoteClose && next == '\'')
                    {
                        Mark(i + 1);
                        _state = State.Code;
                        i += 2;
                        continue;
                    }

                    i++;
                    continue;

                case State.QuotedIdentifier:
                    Mark(i);

                    if (c == '"')
                        _state = State.Code;

                    i++;
                    continue;
            }

            if (c == '-' && next == '-')
            {
                _state = State.LineComment;
                i += 2;
                continue;
            }

            if (c == '/' && next == '*')
            {
                _state = State.BlockComment;
                i += 2;
                continue;
            }

            if (IsQuoteLiteralStart(text, i, out var close))
            {
                Mark(i);
                Mark(i + 1);
                Mark(i + 2);
                _quoteClose = close;
                _state = State.QuotedString;
                i += 3;
                continue;
            }

            Mark(i);

            if (c == '\'')
                _state = State.String;
            else if (c == '"')
                _state = State.QuotedIdentifier;

            i++;
        }

        // A line comment never outlives its line, whether or not the text ended with the newline.
        if (_state == State.LineComment)
            _state = State.Code;

        return (first, last);
    }

    /// <summary>The index of the last character of <paramref name="text"/> that is code, or -1.</summary>
    public static int LastCodeIndex(string text)
    {
        var scanner = new OracleSqlScanner();
        return scanner.Scan(text).Last;
    }

    /// <summary>
    /// <c>q'X...X'</c>, where a bracket opens a literal its partner closes and any other character
    /// closes its own. The <c>q</c> must not be the tail of an identifier: <c>seq'</c> is not one.
    /// </summary>
    private static bool IsQuoteLiteralStart(string text, int i, out char close)
    {
        close = '\0';

        if (text[i] is not ('q' or 'Q') || i + 2 >= text.Length || text[i + 1] != '\'')
            return false;

        var start = i > 0 && text[i - 1] is 'n' or 'N' ? i - 1 : i;
        if (start > 0 && IsIdentifierPart(text[start - 1]))
            return false;

        var open = text[i + 2];
        if (char.IsWhiteSpace(open))
            return false;

        close = open switch
        {
            '[' => ']',
            '{' => '}',
            '(' => ')',
            '<' => '>',
            _ => open,
        };

        return true;
    }

    private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c is '_' or '$' or '#';
}
