using Npgsql;

namespace Grillisoft.Tools.DatabaseDeploy.PostgreSql.Ddl;

internal static class ExceptionExtensions
{
    /// <summary>
    /// The one line worth putting in front of a user, and into the generated script.
    /// </summary>
    /// <remarks>
    /// The SQLSTATE leads because it is the part that is stable across server versions and
    /// locales - the message text is translated, the five character code is not.
    /// </remarks>
    public static string Describe(this Exception exception)
    {
        var innermost = exception;
        while (innermost.InnerException is { } inner)
            innermost = inner;

        return innermost is PostgresException postgres
            ? $"{postgres.SqlState}: {postgres.MessageText.Trim()}"
            : innermost.Message.Trim();
    }
}
