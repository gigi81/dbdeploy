namespace Grillisoft.Tools.DatabaseDeploy.Database.Formatting;

/// <param name="StartsLine">
/// True when this is the first token with content on its source line. The emitter uses it to decide
/// whether a line comment belonged to the code above it or sat on a line of its own.
/// </param>
public readonly record struct SqlToken(SqlTokenKind Kind, string Text, bool StartsLine = false)
{
    public bool IsTrivia => Kind is SqlTokenKind.Whitespace or SqlTokenKind.Newline;

    public bool IsComment => Kind is SqlTokenKind.LineComment or SqlTokenKind.BlockComment;

    public override string ToString() => $"{Kind}:{Text}";
}
