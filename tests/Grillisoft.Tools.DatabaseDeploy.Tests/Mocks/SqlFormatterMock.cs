using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts.Formatting;

namespace Grillisoft.Tools.DatabaseDeploy.Tests.Mocks;

/// <summary>
/// Uppercases the script, which is enough for a test to tell a formatted file from an untouched
/// one without pulling a real dialect into the core tests.
/// </summary>
public class SqlFormatterMock : ISqlFormatter
{
    private readonly string? _verificationError;

    public SqlFormatterMock(string? verificationError = null)
    {
        _verificationError = verificationError;
    }

    public string Dialect => "mock";

    public IList<string> Formatted { get; } = new List<string>();

    public SqlFormatResult Format(string sql, SqlFormatterOptions options)
    {
        Formatted.Add(sql);

        return _verificationError is null
            ? new SqlFormatResult(sql.ToUpperInvariant())
            : new SqlFormatResult(sql, _verificationError);
    }
}
