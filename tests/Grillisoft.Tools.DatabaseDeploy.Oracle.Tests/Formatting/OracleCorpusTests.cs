using Grillisoft.Tools.DatabaseDeploy.Oracle.Formatting;
using Grillisoft.Tools.DatabaseDeploy.Tests.Formatting;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle.Tests.Formatting;

/// <summary>
/// The Oracle examples carry a long SQL*Plus header, PL/SQL program units and <c>/</c> terminators,
/// which is the hardest thing this formatter has to leave intact.
/// </summary>
public class OracleCorpusTests
{
    public static IEnumerable<string> Scripts() => CorpusFiles.For("oracle-01", "oracle-02");

    [Test]
    [MethodDataSource(nameof(Scripts))]
    public Task Format_ShouldVerifyAndBeIdempotent(string relativePath) =>
        CorpusFiles.AssertFormatsCleanly(new OracleFormatter(), relativePath);
}
