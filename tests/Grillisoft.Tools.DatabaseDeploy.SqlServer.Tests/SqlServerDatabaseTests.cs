using Grillisoft.Tools.DatabaseDeploy.Tests.Databases;
using Microsoft.Extensions.Logging;
using Testcontainers.MsSql;
using Xunit.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer.Tests;

public class SqlServerDatabaseTests : DatabaseTest<SqlServerDatabase, MsSqlContainer>
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly CancellationToken _cancellationToken;
    private readonly ILogger<SqlServerDatabase> _logger;

    public SqlServerDatabaseTests(ITestOutputHelper output)
        : base(new MsSqlBuilder().Build())
    {
        _cancellationToken = _cancellationTokenSource.Token;
        _logger = output.BuildLoggerFor<SqlServerDatabase>();
    }

    protected override SqlServerDatabase CreateDatabase()
    {
        return new SqlServerDatabase(
            "test",
            this.ConnectionString,
            "__Migrations",
            60,
            new SqlServerScriptParser(),
            _logger);
    }
}