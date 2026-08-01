namespace Grillisoft.Tools.DatabaseDeploy.Abstractions;

/// <summary>
/// Re-formats the SQL of a deploy or rollback script. Implementations are stateless and registered
/// as singletons, the same way <see cref="IScriptParser"/> is.
/// </summary>
public interface ISqlFormatter
{
    /// <summary>
    /// The provider name of the dialect this formatter lays out, as it would be written for
    /// <c>--provider</c>. Logged for every script, because which dialect a file was formatted with
    /// is worked out from the folder layout and is worth being able to check.
    /// </summary>
    string Dialect { get; }

    SqlFormatResult Format(string sql, SqlFormatterOptions options);
}
