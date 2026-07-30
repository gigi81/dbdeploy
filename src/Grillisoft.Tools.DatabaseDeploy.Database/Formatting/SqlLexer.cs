namespace Grillisoft.Tools.DatabaseDeploy.Database.Formatting;

/// <summary>
/// Splits a script into tokens for the formatter. The scan is lossless: concatenating
/// <see cref="SqlToken.Text"/> over the result reproduces the input byte for byte, which is what
/// lets <see cref="SqlFormatVerifier"/> compare a formatted script against its source.
/// </summary>
public sealed class SqlLexer
{
    /// <summary>Longest first, so that <c>&lt;=</c> never lexes as <c>&lt;</c> then <c>=</c>.</summary>
    private static readonly string[] Operators =
    [
        "->>", "<=>", "<>", "!=", "!<", "!>", ">=", "<=", "||", "::", "->", ":=", "=>",
        "+=", "-=", "*=", "/=", "%=", "|=", "&=", "^=", "**", ".."
    ];

    private readonly SqlDialect _dialect;

    public SqlLexer(SqlDialect dialect)
    {
        _dialect = dialect;
    }

    public List<SqlToken> Tokenize(string sql)
    {
        var tokens = new List<SqlToken>(sql.Length / 4);
        var delimiter = ";";
        var lineStart = true;
        var i = 0;

        while (i < sql.Length)
        {
            var c = sql[i];

            if (c is '\r' or '\n')
            {
                var length = c == '\r' && i + 1 < sql.Length && sql[i + 1] == '\n' ? 2 : 1;
                tokens.Add(new SqlToken(SqlTokenKind.Newline, sql.Substring(i, length)));
                i += length;
                lineStart = true;
                continue;
            }

            if (c is ' ' or '\t')
            {
                var start = i;
                while (i < sql.Length && sql[i] is ' ' or '\t')
                    i++;

                tokens.Add(new SqlToken(SqlTokenKind.Whitespace, sql[start..i]));
                continue;
            }

            // Whole-line constructs are only recognised when nothing but whitespace precedes them,
            // so that a stray "GO" used as a column alias is left alone.
            if (lineStart && TryReadLine(sql, i, ref delimiter, out var lineToken))
            {
                tokens.Add(lineToken);
                i += lineToken.Text.Length;
                lineStart = false;
                continue;
            }

            var token = ReadToken(sql, i, delimiter, out var consumed) with { StartsLine = lineStart };
            tokens.Add(token);
            i += consumed;
            lineStart = false;
        }

        return tokens;
    }

    private bool TryReadLine(string sql, int start, ref string delimiter, out SqlToken token)
    {
        var end = start;
        while (end < sql.Length && sql[end] is not ('\r' or '\n'))
            end++;

        var line = sql[start..end].TrimEnd();

        if (_dialect.IsBatchSeparatorLine(line))
        {
            token = new SqlToken(SqlTokenKind.BatchSeparator, line, StartsLine: true);
            return true;
        }

        if (_dialect.IsPassthroughLine(line))
        {
            if (_dialect.TryReadDelimiterChange(line, out var changed))
                delimiter = changed;

            token = new SqlToken(SqlTokenKind.Passthrough, line, StartsLine: true);
            return true;
        }

        token = default;
        return false;
    }

    private SqlToken ReadToken(string sql, int start, string delimiter, out int consumed)
    {
        var c = sql[start];
        var rest = sql.AsSpan(start);

        // A non-default delimiter separates whole routine bodies, so it outranks everything below:
        // while "delimiter //" is in force the "//" is a separator, not two division operators.
        if (delimiter != ";" && rest.StartsWith(delimiter, StringComparison.Ordinal))
            return Token(SqlTokenKind.BatchSeparator, delimiter, out consumed);

        if (rest.StartsWith("--", StringComparison.Ordinal) ||
            (_dialect.SupportsHashLineComment && c == '#'))
            return Token(SqlTokenKind.LineComment, ReadToEndOfLine(sql, start), out consumed);

        if (rest.StartsWith("/*", StringComparison.Ordinal))
            return Token(SqlTokenKind.BlockComment, ReadBlockComment(sql, start), out consumed);

        if (_dialect.TryReadSpecial(rest, out var kind, out var length))
            return Token(kind, sql.Substring(start, length), out consumed);

        if (c == '\'')
            return Token(SqlTokenKind.StringLiteral, ReadQuoted(sql, start, '\'', '\''), out consumed);

        if (Array.IndexOf(_dialect.IdentifierQuotes, c) >= 0)
            return Token(
                SqlTokenKind.QuotedIdentifier,
                ReadQuoted(sql, start, c, _dialect.ClosingQuote(c)),
                out consumed);

        if (char.IsAsciiDigit(c) || (c == '.' && start + 1 < sql.Length && char.IsAsciiDigit(sql[start + 1])))
            return Token(SqlTokenKind.Number, ReadNumber(sql, start), out consumed);

        if (TryReadPlaceholder(sql, start, out var placeholder))
            return Token(SqlTokenKind.Placeholder, placeholder, out consumed);

        if (IsWordStart(c))
            return Token(SqlTokenKind.Word, ReadWord(sql, start), out consumed);

        switch (c)
        {
            case ';':
                return Token(SqlTokenKind.Terminator, ";", out consumed);
            case ',':
                return Token(SqlTokenKind.Comma, ",", out consumed);
            case '(':
                return Token(SqlTokenKind.OpenParen, "(", out consumed);
            case ')':
                return Token(SqlTokenKind.CloseParen, ")", out consumed);
            default:
                break;
        }

        foreach (var op in Operators)
        {
            if (rest.StartsWith(op, StringComparison.Ordinal))
                return Token(SqlTokenKind.Operator, op, out consumed);
        }

        // Nothing recognised it. Emitting the character verbatim keeps the scan lossless, and the
        // verifier will notice if the emitter then mishandles it.
        return Token(SqlTokenKind.Operator, c.ToString(), out consumed);
    }

    private static SqlToken Token(SqlTokenKind kind, string text, out int consumed)
    {
        consumed = text.Length;
        return new SqlToken(kind, text);
    }

    private static string ReadToEndOfLine(string sql, int start)
    {
        var end = start;
        while (end < sql.Length && sql[end] is not ('\r' or '\n'))
            end++;

        return sql[start..end];
    }

    private static string ReadBlockComment(string sql, int start)
    {
        var end = sql.IndexOf("*/", start + 2, StringComparison.Ordinal);
        return end < 0 ? sql[start..] : sql[start..(end + 2)];
    }

    /// <summary>
    /// Reads a quoted run, treating a doubled closing character as an escape - which is how every
    /// dialect here escapes a quote inside a literal or a delimited identifier.
    /// </summary>
    private string ReadQuoted(string sql, int start, char open, char close)
    {
        var i = start + 1;

        while (i < sql.Length)
        {
            var c = sql[i];

            if (c == '\\' && open == '\'' && _dialect.SupportsBackslashEscapes)
            {
                i += 2;
                continue;
            }

            if (c == close)
            {
                if (i + 1 < sql.Length && sql[i + 1] == close)
                {
                    i += 2;
                    continue;
                }

                return sql[start..(i + 1)];
            }

            i++;
        }

        return sql[start..]; // unterminated; hand back the rest so nothing is lost
    }

    private static string ReadNumber(string sql, int start)
    {
        var i = start;
        while (i < sql.Length && (char.IsAsciiDigit(sql[i]) || sql[i] == '.'))
            i++;

        if (i < sql.Length && (sql[i] is 'e' or 'E'))
        {
            var exponent = i + 1;
            if (exponent < sql.Length && sql[exponent] is '+' or '-')
                exponent++;

            if (exponent < sql.Length && char.IsAsciiDigit(sql[exponent]))
            {
                i = exponent;
                while (i < sql.Length && char.IsAsciiDigit(sql[i]))
                    i++;
            }
        }

        return sql[start..i];
    }

    private static bool TryReadPlaceholder(string sql, int start, out string placeholder)
    {
        placeholder = string.Empty;
        var c = sql[start];

        if (c == '?')
        {
            placeholder = "?";
            return true;
        }

        var i = start;

        if (c == '@')
        {
            while (i < sql.Length && sql[i] == '@') // @@ROWCOUNT and friends
                i++;
        }
        else if (c == ':' && !(start + 1 < sql.Length && sql[start + 1] == ':'))
        {
            i++;
        }
        else if (c == '$' && start + 1 < sql.Length && char.IsAsciiDigit(sql[start + 1]))
        {
            i++;
            while (i < sql.Length && char.IsAsciiDigit(sql[i]))
                i++;

            placeholder = sql[start..i];
            return true;
        }
        else
        {
            return false;
        }

        if (i >= sql.Length || !IsWordStart(sql[i]))
            return false;

        while (i < sql.Length && IsWordPart(sql[i]))
            i++;

        placeholder = sql[start..i];
        return true;
    }

    private static string ReadWord(string sql, int start)
    {
        var i = start;
        while (i < sql.Length && IsWordPart(sql[i]))
            i++;

        return sql[start..i];
    }

    private static bool IsWordStart(char c) => char.IsLetter(c) || c is '_' or '#' or '$';

    private static bool IsWordPart(char c) => char.IsLetterOrDigit(c) || c is '_' or '#' or '$';
}
