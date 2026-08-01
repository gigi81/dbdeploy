using System.Text;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.Database.Formatting;

/// <summary>
/// Walks a token stream once and writes the laid-out script. All the layout decisions described in
/// the design live here; the dialect only supplies the word lists they key off.
/// </summary>
internal sealed class SqlEmitter
{
    private enum FrameKind
    {
        /// <summary>A parenthesised group broken over several lines.</summary>
        Paren,

        /// <summary>BEGIN … END, and the T-SQL TRY/CATCH variants.</summary>
        Begin,

        /// <summary>CASE … END.</summary>
        Case,

        /// <summary>IF … THEN … END IF, in the dialects that spell it that way.</summary>
        If,

        /// <summary>LOOP … END LOOP.</summary>
        Loop
    }

    /// <param name="Indent">The indent in force when the block opened, restored when it closes.</param>
    private readonly record struct Frame(FrameKind Kind, int Indent, int StatementBase, int ClauseIndent);

    private static readonly string[] BlockCloseWords = ["END IF", "END LOOP", "END CASE", "END TRY", "END CATCH", "END"];

    private readonly SqlDialect _dialect;
    private readonly SqlFormatterOptions _options;
    private readonly StringBuilder _output = new();
    private readonly Stack<Frame> _frames = new();

    private List<SqlToken> _tokens = [];
    private int _indent;
    private int _statementBase;
    private int _clauseIndent;
    private int _inlineDepth;
    private int _pendingNewlines;
    private bool _pendingSpace;

    /// <summary>The first keyword of the statement being written, for the contextual clause rules.</summary>
    private string? _statementKeyword;

    /// <summary>Whether a clause keyword has been seen yet, which decides where a DDL '(' goes.</summary>
    private bool _inClause;

    /// <summary>Set by BETWEEN so that the AND belonging to it is not treated as a connective.</summary>
    private bool _pendingBetween;

    /// <summary>
    /// True while inside a statement header that has no clauses, where a word like <c>UPDATE</c> is
    /// a trigger event or a granted privilege rather than the start of a statement. Everything
    /// stays inline until the body begins.
    /// </summary>
    private bool _inHeader;

    public SqlEmitter(SqlDialect dialect, SqlFormatterOptions options)
    {
        _dialect = dialect;
        _options = options;
    }

    public string Emit(List<SqlToken> tokens)
    {
        _tokens = tokens;

        // A while loop rather than a for: a multi-word keyword such as "LEFT OUTER JOIN" is emitted
        // in one go, and EmitWord reports the last token it consumed.
        var i = 0;

        while (i < tokens.Count)
        {
            var token = tokens[i];

            if (KeepsBlankLineBefore(token) && HasBlankLineBefore(i))
                BlankLines(_options.BlankLinesBetweenStatements);

            switch (token.Kind)
            {
                case SqlTokenKind.Whitespace:
                case SqlTokenKind.Newline:
                    break; // the layout is rebuilt from scratch

                case SqlTokenKind.LineComment:
                    EmitLineComment(token);
                    break;

                case SqlTokenKind.BlockComment:
                    EmitBlockComment(token);
                    break;

                case SqlTokenKind.Passthrough:
                    EmitPassthrough(token);
                    break;

                case SqlTokenKind.BatchSeparator:
                    EmitBatchSeparator(token);
                    break;

                case SqlTokenKind.Terminator:
                    EmitTerminator();
                    break;

                case SqlTokenKind.Comma:
                    EmitComma();
                    break;

                case SqlTokenKind.OpenParen:
                    EmitOpenParen(i);
                    break;

                case SqlTokenKind.CloseParen:
                    EmitCloseParen();
                    break;

                case SqlTokenKind.Word:
                    i = EmitWord(i);
                    break;

                case SqlTokenKind.Operator:
                    EmitOperator(i, token.Text);
                    break;

                default:
                    EmitInline(token.Text);
                    break;
            }

            i++;
        }

        return Finish();
    }

    // ---------------------------------------------------------------- words

    /// <returns>The index of the last token consumed, which may be past <paramref name="index"/>.</returns>
    private int EmitWord(int index)
    {
        var last = MatchPhrase(index, out var phrase);

        if (TryEmitBlockClose(phrase))
            return last;

        if (TryEmitControlWord(phrase, last))
            return last;

        if (_inHeader)
        {
            _statementKeyword ??= phrase;
            WritePhrase(index, last);
            Space();
            return last;
        }

        if (_dialect.LineKeywords.Contains(phrase))
        {
            NewLine();
            _indent = _statementBase;
            WritePhrase(index, last);
            Space();
            return last;
        }

        if (_dialect.SetOperatorKeywords.Contains(phrase))
        {
            NewLine();
            _indent = _statementBase;
            WritePhrase(index, last);
            NewLine();
            _clauseIndent = _statementBase;
            return last;
        }

        if (IsClauseKeyword(phrase))
        {
            _statementKeyword ??= phrase;
            _inClause = true;
            NewLine();
            _indent = _statementBase;
            WritePhrase(index, last);
            NewLine();
            _indent = _statementBase + 1;
            _clauseIndent = _indent;
            return last;
        }

        if (_dialect.StatementKeywords.Contains(phrase) && _statementKeyword is null)
        {
            _statementKeyword = phrase;
            _inHeader = IsHeaderOnlyStatement(phrase);
            NewLine();
            _indent = _statementBase;
            WritePhrase(index, last);
            Space();
            return last;
        }

        if (_dialect.ContinuationKeywords.Contains(phrase) && _inlineDepth == 0 && !TakePendingBetween(phrase))
        {
            NewLine();
            _indent = _clauseIndent;
            WritePhrase(index, last);
            Space();
            return last;
        }

        if (phrase.Equals("BETWEEN", StringComparison.OrdinalIgnoreCase))
            _pendingBetween = true;

        // A trigger's event list reads INSERT OR UPDATE OR DELETE. Those are event names, so from
        // here to the body nothing may be laid out as a clause.
        if (phrase.Equals("TRIGGER", StringComparison.OrdinalIgnoreCase) && IsCreateOrAlter())
            _inHeader = true;

        _statementKeyword ??= phrase;
        WritePhrase(index, last);
        Space();
        return last;
    }

    /// <summary>
    /// Statements whose whole body is a header: a privilege list is not a set of clauses, however
    /// much <c>GRANT SELECT, UPDATE ON …</c> looks like one.
    /// </summary>
    private static bool IsHeaderOnlyStatement(string phrase) =>
        phrase.Equals("GRANT", StringComparison.OrdinalIgnoreCase)
        || phrase.Equals("REVOKE", StringComparison.OrdinalIgnoreCase)
        || phrase.StartsWith("COMMENT", StringComparison.OrdinalIgnoreCase);

    private bool IsCreateOrAlter() =>
        _statementKeyword is not null
        && (_statementKeyword.StartsWith("CREATE", StringComparison.OrdinalIgnoreCase)
            || _statementKeyword.StartsWith("ALTER", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// An AND that closes a BETWEEN is part of the predicate, not a connective, and must not start a
    /// new line.
    /// </summary>
    private bool TakePendingBetween(string phrase)
    {
        if (!_pendingBetween || !phrase.Equals("AND", StringComparison.OrdinalIgnoreCase))
            return false;

        _pendingBetween = false;
        return true;
    }

    private bool IsClauseKeyword(string phrase)
    {
        if (!_dialect.ClauseKeywords.Contains(phrase))
            return false;

        if (!_dialect.ContextualClauseKeywords.TryGetValue(phrase, out var required))
            return true;

        return string.Equals(_statementKeyword, required, StringComparison.OrdinalIgnoreCase);
    }

    private bool TryEmitBlockClose(string phrase)
    {
        if (Array.FindIndex(BlockCloseWords, w => w.Equals(phrase, StringComparison.OrdinalIgnoreCase)) < 0)
            return false;

        if (_frames.Count == 0 || _frames.Peek().Kind == FrameKind.Paren)
        {
            // Nothing sensible to close - an END inside an expression, or a block that opened
            // before this script. Keep it inline rather than guessing.
            EmitInline(Case(phrase));
            return true;
        }

        PopFrame();
        NewLine();
        WriteCased(phrase);
        Space();
        return true;
    }

    private bool TryEmitControlWord(string phrase, int last)
    {
        switch (phrase.ToUpperInvariant())
        {
            case "BEGIN" when !OpensTransaction(last):
                _inHeader = false;
                NewLine();
                _indent = _statementBase;
                WriteCased(phrase);
                PushFrame(FrameKind.Begin, _indent + 1);
                NewLine();
                return true;

            case "CASE":
                EmitInline(Case(phrase));
                PushFrame(FrameKind.Case, _indent + 1);
                NewLine();
                return true;

            case "LOOP":
                EmitInline(Case(phrase));
                PushFrame(FrameKind.Loop, _indent + 1);
                NewLine();
                return true;

            case "IF" when _dialect.UsesThenForIf && _statementKeyword is null:
                NewLine();
                _indent = _statementBase;
                WriteCased(phrase);
                Space();
                // The condition may wrap; its continuations sit one level in, which is also where
                // the body lands once THEN arrives.
                PushFrame(FrameKind.If, _indent);
                _clauseIndent = _indent + 1;
                return true;

            case "THEN" when InFrame(FrameKind.If):
                EmitInline(Case(phrase));
                _indent = _frames.Peek().Indent + 1;
                _statementBase = _indent;
                _clauseIndent = _indent;
                NewLine();
                return true;

            case "WHEN" when InFrame(FrameKind.Case):
                NewLine();
                _indent = _frames.Peek().Indent + 1;
                WriteCased(phrase);
                Space();
                return true;

            case "ELSE" or "ELSIF" or "ELSEIF" when InFrame(FrameKind.Case) || InFrame(FrameKind.If):
                EmitElse(phrase);
                return true;

            case "IS" or "AS" when IntroducesBody(last):
                _inHeader = false;
                NewLine();
                _indent = _statementBase;
                WriteCased(phrase);
                NewLine();
                return true;

            default:
                return false;
        }
    }

    private void EmitElse(string phrase)
    {
        var frame = _frames.Peek();

        NewLine();
        _indent = frame.Kind == FrameKind.Case ? frame.Indent + 1 : frame.Indent;
        WriteCased(phrase);

        if (frame.Kind == FrameKind.Case)
        {
            Space();
            return;
        }

        if (phrase.Equals("ELSE", StringComparison.OrdinalIgnoreCase))
        {
            _indent = frame.Indent + 1;
            _statementBase = _indent;
            _clauseIndent = _indent;
            NewLine();
            return;
        }

        Space(); // ELSIF carries a condition, and its THEN re-indents the body
        _clauseIndent = frame.Indent + 1;
    }

    /// <summary>
    /// BEGIN TRANSACTION is a statement, not a block: nothing will ever close it.
    /// </summary>
    private bool OpensTransaction(int last)
    {
        var next = NextSignificant(last);
        if (next < 0 || _tokens[next].Kind != SqlTokenKind.Word)
            return false;

        var word = _tokens[next].Text;
        return word.Equals("TRANSACTION", StringComparison.OrdinalIgnoreCase)
               || word.Equals("TRAN", StringComparison.OrdinalIgnoreCase)
               || word.Equals("DISTRIBUTED", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True for the IS or AS that separates a routine or view header from its body, which reads far
    /// better on a line of its own. Every other AS - a column alias, a cast - stays inline.
    /// </summary>
    private bool IntroducesBody(int last)
    {
        var next = NextSignificant(last);
        if (next < 0 || _tokens[next].Kind != SqlTokenKind.Word)
            return false;

        var word = _tokens[next].Text;
        return word.Equals("BEGIN", StringComparison.OrdinalIgnoreCase)
               || word.Equals("DECLARE", StringComparison.OrdinalIgnoreCase)
               || word.Equals("SELECT", StringComparison.OrdinalIgnoreCase)
               || word.Equals("WITH", StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------ structure

    private void EmitComma()
    {
        _pendingSpace = false;
        Write(",");

        if (BreaksAfterComma())
            NewLine();
        else
            Space();
    }

    /// <summary>
    /// A comma only starts a new line where there is an indented list to line up under - a clause
    /// body or a parenthesised group. At statement level there is nothing to indent to, so
    /// <c>DROP COLUMN First, Last</c> stays on one line rather than dropping to column zero.
    /// </summary>
    private bool BreaksAfterComma()
    {
        if (_inlineDepth > 0)
            return false;

        return _indent > _statementBase || InFrame(FrameKind.Paren);
    }

    private void EmitOpenParen(int index)
    {
        var inline = _inlineDepth > 0 || CanInline(index);

        if (inline)
        {
            if (!WantsSpaceBeforeParen(index))
                _pendingSpace = false;

            Write("(");
            _inlineDepth++;
            return;
        }

        if (ParenBelongsOnItsOwnLine())
        {
            NewLine();
            _indent = _statementBase;
        }
        else if (!WantsSpaceBeforeParen(index))
        {
            _pendingSpace = false;
        }

        Write("(");
        PushFrame(FrameKind.Paren, _indent + 1);
        NewLine();
    }

    private void EmitCloseParen()
    {
        if (_inlineDepth > 0)
        {
            _inlineDepth--;
            _pendingSpace = false;
            Write(")");
            Space();
            return;
        }

        if (_frames.Count > 0 && _frames.Peek().Kind == FrameKind.Paren)
            PopFrame();

        NewLine();
        Write(")");
        Space();
    }

    /// <summary>
    /// The column list of a CREATE or ALTER goes on a line of its own, the way the checked-in
    /// scripts already write it. A parenthesis anywhere else - a subquery, an IN list, a call -
    /// stays on the line it started on.
    /// </summary>
    private bool ParenBelongsOnItsOwnLine() => !_inClause && !_inHeader && IsCreateOrAlter();

    /// <summary>
    /// <c>COUNT(*)</c> and <c>NVARCHAR(30)</c> close up; <c>IN (…)</c> and <c>VALUES (…)</c> keep
    /// their space.
    /// </summary>
    private bool WantsSpaceBeforeParen(int index)
    {
        var previous = PreviousSignificant(index);
        if (previous < 0)
            return false;

        var token = _tokens[previous];

        if (token.Kind != SqlTokenKind.Word)
            return token.Kind is not (SqlTokenKind.QuotedIdentifier or SqlTokenKind.CloseParen);

        return _dialect.Reserved.Contains(token.Text) && !_dialect.DataTypes.Contains(token.Text);
    }

    private void EmitTerminator()
    {
        _pendingSpace = false;
        Write(";");

        _statementKeyword = null;
        _inClause = false;
        _inHeader = false;
        _pendingBetween = false;
        _indent = _statementBase;
        _clauseIndent = _statementBase;

        if (InAnyBlock())
            NewLine();
        else
            BlankLines(_options.BlankLinesBetweenStatements);
    }

    private void EmitBatchSeparator(SqlToken token)
    {
        _frames.Clear();
        _inlineDepth = 0;
        _indent = 0;
        _statementBase = 0;
        _clauseIndent = 0;
        _statementKeyword = null;
        _inClause = false;
        _inHeader = false;
        _pendingBetween = false;

        // The separator closes the batch above it, so it belongs directly under it. Any blank line
        // the terminator just queued is dropped; the blank line goes after instead.
        ForceNewLine();
        Write(Apply(token.Text, _options.BatchSeparatorCase));
        BlankLines(_options.BlankLinesBetweenStatements);
    }

    /// <summary>
    /// Whether a blank line the author left in front of this token is worth keeping. It is in front
    /// of the things a blank line separates - a comment, a SQL*Plus line, the start of a statement -
    /// and nowhere else: inside a statement the layout is rebuilt from the tokens, and a blank line
    /// in the middle of a reflowed select list is not something to reproduce. A batch separator is
    /// written directly under the batch it closes, so a blank line in front of one is dropped either
    /// way.
    /// </summary>
    private bool KeepsBlankLineBefore(SqlToken token)
    {
        if (_options.BlankLinesBetweenStatements <= 0)
            return false;

        return token.Kind switch
        {
            SqlTokenKind.Passthrough => true,
            SqlTokenKind.LineComment or SqlTokenKind.BlockComment => token.StartsLine,
            SqlTokenKind.BatchSeparator or SqlTokenKind.Terminator or SqlTokenKind.Comma => false,
            SqlTokenKind.Whitespace or SqlTokenKind.Newline => false,
            _ => token.StartsLine && AtStatementStart()
        };
    }

    private bool AtStatementStart() =>
        _statementKeyword is null && _frames.Count == 0 && _inlineDepth == 0;

    /// <summary>
    /// Whether the author left a blank line in front of the token at <paramref name="index"/>: two
    /// line breaks with nothing but spaces between them, after something that was already written.
    /// The trivia is thrown away when the layout is rebuilt, so this is all that is left of it.
    /// </summary>
    private bool HasBlankLineBefore(int index)
    {
        var newlines = 0;

        for (var i = index - 1; i >= 0; i--)
        {
            switch (_tokens[i].Kind)
            {
                case SqlTokenKind.Newline:
                    newlines++;
                    break;

                case SqlTokenKind.Whitespace:
                    break;

                // Leading blank lines have nothing above them to be separated from.
                default:
                    return newlines >= 2;
            }
        }

        return false;
    }

    private void EmitPassthrough(SqlToken token)
    {
        var indent = _indent;
        _indent = 0;
        NewLine();
        Write(token.Text);
        NewLine();
        _indent = indent;
    }

    private void EmitLineComment(SqlToken token)
    {
        if (token.StartsLine)
            NewLine();
        else
            Space();

        Write(token.Text.TrimEnd());
        NewLine();
    }

    private void EmitBlockComment(SqlToken token)
    {
        NewLine();
        Write(ReindentComment(token.Text));
        NewLine();
    }

    /// <summary>
    /// Puts the continuation lines of a block comment under its opener, keeping whatever line
    /// endings the comment itself was written with.
    /// </summary>
    private string ReindentComment(string comment)
    {
        var lines = comment.Split('\n');
        if (lines.Length == 1)
            return comment;

        var builder = new StringBuilder(comment.Length);
        var indent = IndentText(_indent);

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

        return builder.ToString();
    }

    // ----------------------------------------------------------- primitives

    private void EmitInline(string text)
    {
        Write(text);
        Space();
    }

    /// <summary>
    /// Member access closes up, a leading sign binds to its operand, and everything else gets a
    /// space either side.
    /// </summary>
    private void EmitOperator(int index, string text)
    {
        if (_dialect.IsTightOperator(text))
        {
            _pendingSpace = false;
            Write(text);
            return;
        }

        Write(text);

        if (IsUnarySign(index, text))
            _pendingSpace = false;
        else
            Space();
    }

    private bool IsUnarySign(int index, string text)
    {
        if (text is not ("-" or "+" or "~"))
            return false;

        var previous = PreviousSignificant(index);

        return previous < 0
               || _tokens[previous].Kind is SqlTokenKind.OpenParen or SqlTokenKind.Comma
                   or SqlTokenKind.Operator;
    }

    private void WritePhrase(int first, int last)
    {
        for (var i = first; i <= last; i++)
        {
            if (_tokens[i].Kind != SqlTokenKind.Word)
                continue;

            Write(CaseAt(i));

            if (i < last)
                Space();
        }
    }

    private void WriteCased(string phrase)
    {
        var words = phrase.Split(' ');

        for (var i = 0; i < words.Length; i++)
        {
            Write(Case(words[i]));
            if (i < words.Length - 1)
                Space();
        }
    }

    private void Write(string text)
    {
        if (text.Length == 0)
            return;

        if (_pendingNewlines > 0)
        {
            if (_output.Length > 0)
            {
                for (var i = 0; i < _pendingNewlines; i++)
                    _output.Append(_options.NewLine);
            }

            _pendingNewlines = 0;
            _pendingSpace = false;
            _output.Append(IndentText(_indent));
        }
        else if (_pendingSpace && _output.Length > 0)
        {
            _output.Append(' ');
        }

        _pendingSpace = false;
        _output.Append(text);
    }

    private void Space() => _pendingSpace = true;

    private void NewLine()
    {
        _pendingNewlines = Math.Max(_pendingNewlines, 1);
        _pendingSpace = false;
    }

    /// <summary>
    /// Exactly one line break, discarding any blank line already queued.
    /// </summary>
    private void ForceNewLine()
    {
        _pendingNewlines = 1;
        _pendingSpace = false;
    }

    private void BlankLines(int count)
    {
        _pendingNewlines = Math.Max(_pendingNewlines, count + 1);
        _pendingSpace = false;
    }

    private string IndentText(int level) =>
        level <= 0 ? string.Empty : string.Concat(Enumerable.Repeat(_options.Indent, level));

    private string Finish()
    {
        var text = _output.ToString().TrimEnd();

        if (_options.InsertFinalNewline && text.Length > 0)
            text += _options.NewLine;

        return text;
    }

    // ---------------------------------------------------------------- frames

    private void PushFrame(FrameKind kind, int indent)
    {
        _frames.Push(new Frame(kind, _indent, _statementBase, _clauseIndent));
        _indent = indent;
        _statementBase = indent;
        _clauseIndent = indent;
        _statementKeyword = null;
        _inClause = false;
        _inHeader = false;
    }

    private void PopFrame()
    {
        var frame = _frames.Pop();
        _indent = frame.Indent;
        _statementBase = frame.StatementBase;
        _clauseIndent = frame.ClauseIndent;
        _statementKeyword = null;
        _inClause = false;
        _inHeader = false;
    }

    private bool InFrame(FrameKind kind) => _frames.Count > 0 && _frames.Peek().Kind == kind;

    private bool InAnyBlock()
    {
        foreach (var frame in _frames)
        {
            if (frame.Kind != FrameKind.Paren)
                return true;
        }

        return false;
    }

    // --------------------------------------------------------------- lookups

    /// <summary>
    /// Matches the longest run of words that any of the dialect's sets knows, so that
    /// <c>LEFT OUTER JOIN</c> and <c>END IF</c> are laid out as one keyword.
    /// </summary>
    private int MatchPhrase(int index, out string phrase)
    {
        var words = new int[4];
        var count = 0;

        for (var i = index; i < _tokens.Count && count < words.Length; i++)
        {
            if (_tokens[i].IsTrivia)
                continue;

            if (_tokens[i].Kind != SqlTokenKind.Word)
                break;

            words[count++] = i;
        }

        var builder = new StringBuilder();

        for (var length = count; length > 1; length--)
        {
            builder.Clear();

            for (var i = 0; i < length; i++)
            {
                if (i > 0)
                    builder.Append(' ');

                builder.Append(_tokens[words[i]].Text);
            }

            var candidate = builder.ToString();

            if (IsKnownPhrase(candidate))
            {
                phrase = candidate;
                return words[length - 1];
            }
        }

        phrase = _tokens[index].Text;
        return index;
    }

    private bool IsKnownPhrase(string candidate) =>
        _dialect.ClauseKeywords.Contains(candidate)
        || _dialect.LineKeywords.Contains(candidate)
        || _dialect.StatementKeywords.Contains(candidate)
        || _dialect.ContinuationKeywords.Contains(candidate)
        || _dialect.SetOperatorKeywords.Contains(candidate)
        || Array.FindIndex(BlockCloseWords, w => w.Equals(candidate, StringComparison.OrdinalIgnoreCase)) >= 0;

    private int NextSignificant(int index)
    {
        for (var i = index + 1; i < _tokens.Count; i++)
        {
            if (!_tokens[i].IsTrivia)
                return i;
        }

        return -1;
    }

    private int PreviousSignificant(int index)
    {
        for (var i = index - 1; i >= 0; i--)
        {
            if (!_tokens[i].IsTrivia)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Decides whether a parenthesised group is short and simple enough to stay on one line.
    /// </summary>
    private bool CanInline(int open)
    {
        var length = CurrentColumn();
        var depth = 0;

        for (var i = open; i < _tokens.Count; i++)
        {
            var token = _tokens[i];

            if (token.IsTrivia)
                continue;

            if (token.IsComment
                || token.Kind is SqlTokenKind.Passthrough or SqlTokenKind.BatchSeparator or SqlTokenKind.Terminator)
                return false;

            if (token.Kind == SqlTokenKind.Word && IsBlockingWord(token.Text))
                return false;

            length += token.Text.Length + 1;
            if (length > _options.MaxLineLength)
                return false;

            switch (token.Kind)
            {
                case SqlTokenKind.OpenParen:
                    depth++;
                    break;

                case SqlTokenKind.CloseParen:
                    depth--;
                    if (depth == 0)
                        return true;

                    break;

                default:
                    break;
            }
        }

        return false;
    }

    /// <summary>A word that always forces its own line, so the group around it cannot be inline.</summary>
    private bool IsBlockingWord(string word) =>
        _dialect.ClauseKeywords.Contains(word)
        || _dialect.LineKeywords.Contains(word)
        || _dialect.SetOperatorKeywords.Contains(word)
        || word.Equals("BEGIN", StringComparison.OrdinalIgnoreCase)
        || word.Equals("CASE", StringComparison.OrdinalIgnoreCase)
        || word.Equals("LOOP", StringComparison.OrdinalIgnoreCase)
        || word.Equals("END", StringComparison.OrdinalIgnoreCase);

    private int CurrentColumn()
    {
        if (_pendingNewlines > 0)
            return IndentText(_indent).Length;

        for (var i = _output.Length - 1; i >= 0; i--)
        {
            if (_output[i] == '\n')
                return _output.Length - i - 1;
        }

        return _output.Length;
    }

    // ---------------------------------------------------------------- casing

    /// <summary>
    /// Cases the word at <paramref name="index"/>. A word the dialect does not know is only a
    /// function name when a parenthesis follows it; anything else is an identifier and is left
    /// exactly as the author wrote it.
    /// </summary>
    private string CaseAt(int index)
    {
        var word = _tokens[index].Text;

        if (_dialect.DataTypes.Contains(word) || _dialect.Reserved.Contains(word))
            return Case(word);

        return _dialect.Functions.Contains(word)
            ? Apply(word, _options.FunctionCase)
            : word;
    }

    private string Case(string word)
    {
        if (_dialect.DataTypes.Contains(word))
            return Apply(word, _options.DataTypeCase);

        return _dialect.Reserved.Contains(word)
            ? Apply(word, _options.KeywordCase)
            : word;
    }

    private static string Apply(string word, SqlCase casing) => casing switch
    {
        SqlCase.Upper => word.ToUpperInvariant(),
        SqlCase.Lower => word.ToLowerInvariant(),
        _ => word
    };
}
