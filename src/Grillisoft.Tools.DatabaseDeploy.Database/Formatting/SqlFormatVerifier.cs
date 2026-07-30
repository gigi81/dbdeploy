using System.Text;

namespace Grillisoft.Tools.DatabaseDeploy.Database.Formatting;

/// <summary>
/// The guard rail around a re-flow formatter. Layout may change freely, but the significant tokens
/// must not: a formatter that drops a comment, swallows a statement or invents a parenthesis has
/// corrupted a migration script, and the only safe response is to leave the file alone.
/// </summary>
public static class SqlFormatVerifier
{
    /// <summary>
    /// Compares two token streams ignoring whitespace, keyword casing and comment indentation.
    /// </summary>
    /// <returns>Null when the streams are equivalent, otherwise a description of the first difference.</returns>
    public static string? Verify(IReadOnlyList<SqlToken> before, IReadOnlyList<SqlToken> after)
    {
        var source = Significant(before);
        var result = Significant(after);

        for (var i = 0; i < Math.Min(source.Count, result.Count); i++)
        {
            if (!AreEquivalent(source[i], result[i]))
                return $"token {i + 1} changed from {Describe(source[i])} to {Describe(result[i])}";
        }

        if (source.Count > result.Count)
            return $"{source.Count - result.Count} token(s) were lost, starting with {Describe(source[result.Count])}";

        if (result.Count > source.Count)
            return $"{result.Count - source.Count} token(s) appeared, starting with {Describe(result[source.Count])}";

        return null;
    }

    private static List<SqlToken> Significant(IReadOnlyList<SqlToken> tokens)
    {
        var significant = new List<SqlToken>(tokens.Count);

        foreach (var token in tokens)
        {
            if (!token.IsTrivia)
                significant.Add(token);
        }

        return significant;
    }

    private static bool AreEquivalent(SqlToken source, SqlToken result)
    {
        if (source.Kind != result.Kind)
            return false;

        return source.Kind switch
        {
            // Casing these is the formatter's job.
            SqlTokenKind.Word or SqlTokenKind.BatchSeparator =>
                string.Equals(source.Text, result.Text, StringComparison.OrdinalIgnoreCase),

            // Comments keep their content but may be re-indented with the code around them.
            SqlTokenKind.LineComment or SqlTokenKind.BlockComment =>
                string.Equals(Normalize(source.Text), Normalize(result.Text), StringComparison.Ordinal),

            _ => string.Equals(source.Text, result.Text, StringComparison.Ordinal)
        };
    }

    /// <summary>Strips the leading and trailing whitespace of every line of a comment.</summary>
    private static string Normalize(string comment)
    {
        var builder = new StringBuilder(comment.Length);
        var first = true;

        foreach (var line in comment.Split('\n'))
        {
            if (!first)
                builder.Append('\n');

            builder.Append(line.Trim());
            first = false;
        }

        return builder.ToString();
    }

    private static string Describe(SqlToken token)
    {
        var text = token.Text.Replace("\r", "\\r").Replace("\n", "\\n");
        if (text.Length > 40)
            text = text[..40] + "…";

        return $"{token.Kind} '{text}'";
    }
}
