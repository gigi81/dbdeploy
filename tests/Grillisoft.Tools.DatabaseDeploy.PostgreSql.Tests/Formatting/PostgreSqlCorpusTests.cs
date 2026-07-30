using AwesomeAssertions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
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
    public async Task Format_ShouldVerifyAndBeIdempotent(string relativePath)
    {
        var sql = await File.ReadAllTextAsync(CorpusFiles.Resolve(relativePath));
        var formatter = new PostgreSqlFormatter();
        var options = SqlFormatterOptions.Default with { NewLine = "\n" };

        var first = formatter.Format(sql, options);
        _output.WriteLine(first.Sql);

        first.VerificationError.Should().BeNull();

        var second = formatter.Format(first.Sql, options);

        second.VerificationError.Should().BeNull();
        second.Sql.Should().Be(first.Sql, "formatting an already formatted script must change nothing");
    }
}
