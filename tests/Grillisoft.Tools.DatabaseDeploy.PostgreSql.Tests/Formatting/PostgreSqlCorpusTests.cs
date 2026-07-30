using Grillisoft.Tools.DatabaseDeploy.PostgreSql.Formatting;
using Grillisoft.Tools.DatabaseDeploy.Tests.Formatting;
using Xunit;
using Xunit.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.PostgreSql.Tests.Formatting;

public class PostgreSqlCorpusTests
{
    private readonly ITestOutputHelper _output;

    public PostgreSqlCorpusTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public static TheoryData<string> Scripts => CorpusFiles.For("postgres-01");

    [Theory]
    [MemberData(nameof(Scripts))]
    public Task Format_ShouldVerifyAndBeIdempotent(string relativePath) =>
        CorpusFiles.AssertFormatsCleanly(new PostgreSqlFormatter(), relativePath, _output);
}
