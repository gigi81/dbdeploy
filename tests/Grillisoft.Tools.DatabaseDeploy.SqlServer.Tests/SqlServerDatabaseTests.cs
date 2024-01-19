using Grillisoft.Tools.DatabaseDeploy.Tests.Databases;
using Testcontainers.MsSql;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer.Tests;

public class SqlServerDatabaseTests : DatabaseTest<SqlServerDatabase, MsSqlContainer>
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly CancellationToken _cancellationToken;

    public SqlServerDatabaseTests()
        : base(new MsSqlBuilder().Build())
    {
        _cancellationToken = _cancellationTokenSource.Token;
    }

    protected override SqlServerDatabase CreateDatabase()
    {
        return new SqlServerDatabase(
            "test",
            this.ConnectionString,
            "__Migrations",
            new SqlServerScriptParser());
    }
}