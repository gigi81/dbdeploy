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
    public Task Format_ShouldVerifyAndBeIdempotent(string relativePath) =>
        CorpusFiles.AssertFormatsCleanly(new OracleFormatter(), relativePath, _output);
}
