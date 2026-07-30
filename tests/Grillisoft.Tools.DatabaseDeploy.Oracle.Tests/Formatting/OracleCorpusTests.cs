using AwesomeAssertions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Oracle.Formatting;
using Grillisoft.Tools.DatabaseDeploy.Tests.Formatting;
using Xunit;
using Xunit.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle.Tests.Formatting;

/// <summary>
/// The Oracle examples carry a long SQL*Plus header, PL/SQL program units and <c>/</c> terminators,
/// which is the hardest thing this formatter has to leave intact.
/// </summary>
public class OracleCorpusTests
{
    private readonly ITestOutputHelper _output;

    public OracleCorpusTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public static TheoryData<string> Scripts => CorpusFiles.For("oracle-01", "oracle-02");

    [Theory]
    [MemberData(nameof(Scripts))]
    public async Task Format_ShouldVerifyAndBeIdempotent(string relativePath)
    {
        var sql = await File.ReadAllTextAsync(CorpusFiles.Resolve(relativePath));
        var formatter = new OracleFormatter();
        var options = SqlFormatterOptions.Default with { NewLine = "\n" };

        var first = formatter.Format(sql, options);
        _output.WriteLine(first.Sql);

        first.VerificationError.Should().BeNull();

        var second = formatter.Format(first.Sql, options);

        second.VerificationError.Should().BeNull();
        second.Sql.Should().Be(first.Sql, "formatting an already formatted script must change nothing");
    }
}
