using Microsoft.Data.SqlClient;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer;

internal static class ExceptionExtensions
{
    /// <summary>
    /// The one line worth putting in front of a user, and into the generated script.
    /// </summary>
    /// <remarks>
    /// SMO buries the reason inside a chain of wrapper exceptions - a <c>FailedOperationException</c>
    /// around an <c>ExecutionFailureException</c> around the <c>SqlException</c> that actually says
    /// what the server refused - so only the innermost one is of any use.
    /// </remarks>
    public static string Describe(this Exception exception)
    {
        var innermost = exception;
        while (innermost.InnerException is { } inner)
            innermost = inner;

        return innermost is SqlException sql
            ? $"Msg {sql.Number}: {sql.Message.Trim()}"
            : innermost.Message.Trim();
    }
}
