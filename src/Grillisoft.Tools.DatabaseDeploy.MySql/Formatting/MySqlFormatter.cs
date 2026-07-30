using Grillisoft.Tools.DatabaseDeploy.Database.Formatting;

namespace Grillisoft.Tools.DatabaseDeploy.MySql.Formatting;

internal sealed class MySqlFormatter : SqlReflowFormatter
{
    /// <summary>The one instance needed: the formatter holds no state.</summary>
    public static readonly MySqlFormatter Instance = new();

    public MySqlFormatter()
        : base(new MySqlDialect())
    {
    }
}
