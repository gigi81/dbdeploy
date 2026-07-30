using AwesomeAssertions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.MySql.Formatting;
using Grillisoft.Tools.DatabaseDeploy.Tests.Formatting;
using Xunit;
using Xunit.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.MySql.Tests.Formatting;

/// <summary>
/// The MySQL examples switch the delimiter around routine bodies and use <c>#</c> comments and
/// backtick identifiers, none of which a naive formatter survives.
/// </summary>
public class MySqlCorpusTests
{
    private readonly ITestOutputHelper _output;

    public MySqlCorpusTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public static TheoryData<string> Scripts => CorpusFiles.For("mysql-01");

    [Theory]
    [MemberData(nameof(Scripts))]
    public async Task Format_ShouldVerifyAndBeIdempotent(string relativePath)
    {
        var sql = await File.ReadAllTextAsync(CorpusFiles.Resolve(relativePath));
        var formatter = new MySqlFormatter();
        var options = SqlFormatterOptions.Default with { NewLine = "\n" };

        var first = formatter.Format(sql, options);
        _output.WriteLine(first.Sql);

        first.VerificationError.Should().BeNull();

        var second = formatter.Format(first.Sql, options);

        second.VerificationError.Should().BeNull();
        second.Sql.Should().Be(first.Sql, "formatting an already formatted script must change nothing");
    }
}
