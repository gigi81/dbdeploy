using System.Collections.Frozen;
using Grillisoft.Tools.DatabaseDeploy.Database.Formatting;

namespace Grillisoft.Tools.DatabaseDeploy.Database.Tests.Formatting;

/// <summary>
/// A plain ANSI dialect, so the shared lexer and verifier can be tested without dragging a provider
/// in. Individual features are switched on by the constructor where a test needs them.
/// </summary>
internal sealed class TestDialect : SqlDialect
{
    public TestDialect(
        char[]? identifierQuotes = null,
        bool hashComments = false,
        bool backslashEscapes = false,
        string? batchSeparator = null)
    {
        IdentifierQuotes = identifierQuotes ?? ['"'];
        SupportsHashLineComment = hashComments;
        SupportsBackslashEscapes = backslashEscapes;
        BatchSeparator = batchSeparator;
    }

    public override FrozenSet<string> StatementKeywords { get; } = SqlKeywords.Set(SqlKeywords.Statement);

    public override FrozenSet<string> ClauseKeywords { get; } = SqlKeywords.Set(SqlKeywords.Clause);

    public override FrozenSet<string> ContinuationKeywords { get; } = SqlKeywords.Set(SqlKeywords.Continuation);

    public override FrozenSet<string> SetOperatorKeywords { get; } = SqlKeywords.Set(SqlKeywords.SetOperator);

    public override FrozenSet<string> Reserved { get; } = SqlKeywords.Set(
        SqlKeywords.Reserved,
        SqlKeywords.Statement,
        SqlKeywords.Clause,
        SqlKeywords.Line,
        SqlKeywords.Continuation,
        SqlKeywords.SetOperator,
        SqlKeywords.BlockOpen);

    public override FrozenSet<string> DataTypes { get; } = SqlKeywords.Set(SqlKeywords.DataTypes);

    public override char[] IdentifierQuotes { get; }

    public override bool SupportsHashLineComment { get; }

    public override bool SupportsBackslashEscapes { get; }

    public override string? BatchSeparator { get; }
}
