using Grillisoft.Tools.DatabaseDeploy.MySql.Formatting;
using Grillisoft.Tools.DatabaseDeploy.Tests.Formatting;

namespace Grillisoft.Tools.DatabaseDeploy.MySql.Tests.Formatting;

/// <summary>
/// The MySQL examples switch the delimiter around routine bodies and use <c>#</c> comments and
/// backtick identifiers, none of which a naive formatter survives.
/// </summary>
public class MySqlCorpusTests
{
    public static IEnumerable<string> Scripts() => CorpusFiles.For("mysql-01");

    [Test]
    [MethodDataSource(nameof(Scripts))]
    public Task Format_ShouldVerifyAndBeIdempotent(string relativePath) =>
        CorpusFiles.AssertFormatsCleanly(new MySqlFormatter(), relativePath);
}
