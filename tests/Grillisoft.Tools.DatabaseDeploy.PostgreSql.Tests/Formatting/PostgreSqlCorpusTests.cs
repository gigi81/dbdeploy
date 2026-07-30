using Grillisoft.Tools.DatabaseDeploy.PostgreSql.Formatting;
using Grillisoft.Tools.DatabaseDeploy.Tests.Formatting;

namespace Grillisoft.Tools.DatabaseDeploy.PostgreSql.Tests.Formatting;

public class PostgreSqlCorpusTests
{
    public static IEnumerable<string> Scripts() => CorpusFiles.For("postgres-01");

    [Test]
    [MethodDataSource(nameof(Scripts))]
    public Task Format_ShouldVerifyAndBeIdempotent(string relativePath) =>
        CorpusFiles.AssertFormatsCleanly(new PostgreSqlFormatter(), relativePath);
}
