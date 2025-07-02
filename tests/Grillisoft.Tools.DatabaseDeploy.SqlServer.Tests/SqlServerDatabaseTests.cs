using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Tests.Databases;
using Testcontainers.MsSql;
using Xunit.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer.Tests;

public class SqlServerDatabaseTests : DatabaseTest<SqlServerDatabase, MsSqlContainer>
{
    public SqlServerDatabaseTests(ITestOutputHelper output)
        : base(new MsSqlBuilder().Build(), output)
    {
    }

    protected override IDatabaseFactory CreateDatabaseFactory()
    {
        return new SqlServerDatabaseFactory(
            new SqlServerScriptParser(),
            this.GlobalSettingsOptions,
            this.LoggerFactory);
    }

    protected override string ProviderName => SqlServerDatabaseFactory.ProviderName;
}