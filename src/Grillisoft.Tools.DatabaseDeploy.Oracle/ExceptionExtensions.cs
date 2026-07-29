using Oracle.ManagedDataAccess.Client;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle;

internal static class ExceptionExtensions
{
    /// <summary>
    /// The one line worth putting in front of a user, and into the generated script. The ORA number
    /// is what anybody diagnosing the failure will search for, so it leads.
    /// </summary>
    public static string Describe(this Exception exception)
        => exception is OracleException oracle
            ? $"ORA-{oracle.Number:00000}: {oracle.Message.Trim()}"
            : exception.Message.Trim();
}
