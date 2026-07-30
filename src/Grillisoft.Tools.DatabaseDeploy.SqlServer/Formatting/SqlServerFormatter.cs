using Grillisoft.Tools.DatabaseDeploy.Database.Formatting;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer.Formatting;

internal sealed class SqlServerFormatter : SqlReflowFormatter
{
    /// <summary>The one instance needed: the formatter holds no state.</summary>
    public static readonly SqlServerFormatter Instance = new();

    public SqlServerFormatter()
        : base(new SqlServerDialect())
    {
    }
}
