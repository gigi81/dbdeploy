using AwesomeAssertions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.SqlServer.Formatting;
using Grillisoft.Tools.DatabaseDeploy.Tests.Formatting;
using Xunit;
using Xunit.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer.Tests.Formatting;

/// <summary>
/// Runs every checked-in T-SQL example through the formatter. These files are real migration
/// scripts, so they exercise batch separators, comments and DDL that no hand-written case covers.
/// Two properties have to hold for each: the result verifies against its source, and formatting it
/// again changes nothing.
/// </summary>
public class SqlServerCorpusTests
{
    private readonly ITestOutputHelper _output;

    public SqlServerCorpusTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public static TheoryData<string> Scripts => CorpusFiles.For("mssql-01", "mssql-02");

    [Theory]
    [MemberData(nameof(Scripts))]
    public async Task Format_ShouldVerifyAndBeIdempotent(string relativePath)
    {
        var sql = await File.ReadAllTextAsync(CorpusFiles.Resolve(relativePath));
        var formatter = new SqlServerFormatter();
        var options = SqlFormatterOptions.Default with { NewLine = "\n" };

        var first = formatter.Format(sql, options);
        _output.WriteLine(first.Sql);

        first.VerificationError.Should().BeNull();

        var second = formatter.Format(first.Sql, options);

        second.VerificationError.Should().BeNull();
        second.Sql.Should().Be(first.Sql, "formatting an already formatted script must change nothing");
    }
}
