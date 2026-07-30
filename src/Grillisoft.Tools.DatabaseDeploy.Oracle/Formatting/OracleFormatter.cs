using Grillisoft.Tools.DatabaseDeploy.Database.Formatting;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle.Formatting;

internal sealed class OracleFormatter : SqlReflowFormatter
{
    /// <summary>The one instance needed: the formatter holds no state.</summary>
    public static readonly OracleFormatter Instance = new();

    public OracleFormatter()
        : base(new OracleDialect())
    {
    }
}
