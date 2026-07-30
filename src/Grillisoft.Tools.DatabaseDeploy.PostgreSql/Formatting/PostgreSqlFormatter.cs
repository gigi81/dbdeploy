using Grillisoft.Tools.DatabaseDeploy.Database.Formatting;

namespace Grillisoft.Tools.DatabaseDeploy.PostgreSql.Formatting;

internal sealed class PostgreSqlFormatter : SqlReflowFormatter
{
    /// <summary>The one instance needed: the formatter holds no state.</summary>
    public static readonly PostgreSqlFormatter Instance = new();

    public PostgreSqlFormatter()
        : base(new PostgreSqlDialect())
    {
    }
}
