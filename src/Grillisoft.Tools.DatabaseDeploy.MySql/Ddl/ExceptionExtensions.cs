using MySqlConnector;

namespace Grillisoft.Tools.DatabaseDeploy.MySql.Ddl;

internal static class ExceptionExtensions
{
    /// <summary>
    /// The one line worth putting in front of a user, and into the generated script. The error
    /// number is what a MySQL or MariaDB manual is searched by, so it leads.
    /// </summary>
    public static string Describe(this Exception exception)
    {
        var innermost = exception;
        while (innermost.InnerException is { } inner)
            innermost = inner;

        return innermost is MySqlException mysql
            ? $"Error {mysql.Number}: {mysql.Message.Trim()}"
            : innermost.Message.Trim();
    }
}
