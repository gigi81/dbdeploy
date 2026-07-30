namespace Grillisoft.Tools.DatabaseDeploy.Abstractions;

/// <summary>
/// Re-formats the SQL of a deploy or rollback script. Implementations are stateless and registered
/// as singletons, the same way <see cref="IScriptParser"/> is.
/// </summary>
public interface ISqlFormatter
{
    SqlFormatResult Format(string sql, SqlFormatterOptions options);
}
