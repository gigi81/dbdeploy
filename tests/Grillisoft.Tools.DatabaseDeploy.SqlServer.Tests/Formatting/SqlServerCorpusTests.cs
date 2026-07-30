using Grillisoft.Tools.DatabaseDeploy.SqlServer.Formatting;
using Grillisoft.Tools.DatabaseDeploy.Tests.Formatting;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer.Tests.Formatting;

/// <summary>
/// Runs every checked-in T-SQL example through the formatter. These files are real migration
/// scripts, so they exercise batch separators, comments and DDL that no hand-written case covers.
/// Two properties have to hold for each: the result verifies against its source, and formatting it
/// again changes nothing.
/// </summary>
public class SqlServerCorpusTests
{
    public static IEnumerable<string> Scripts() => CorpusFiles.For("mssql-01", "mssql-02");

    [Test]
    [MethodDataSource(nameof(Scripts))]
    public Task Format_ShouldVerifyAndBeIdempotent(string relativePath) =>
        CorpusFiles.AssertFormatsCleanly(new SqlServerFormatter(), relativePath);
}
