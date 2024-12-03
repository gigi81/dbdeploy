using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Tests.Databases;
using Testcontainers.PostgreSql;
using Xunit.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.PostgreSql.Tests;

public class PostgreSqlDatabaseTests : DatabaseTest<PostgreSqlDatabase, PostgreSqlContainer>
{
    public PostgreSqlDatabaseTests(ITestOutputHelper output)
        : base(new PostgreSqlBuilder().Build(), output)
    {
    }

    protected override IDatabaseFactory CreateDatabaseFactory()
    {
        return new PostgreSqlDatabaseFactory(
            new PostgreSqlScriptParser(),
            this.GlobalSettingsOptions,
            this.LoggerFactory);
    }

    protected override string ProviderName => PostgreSqlDatabaseFactory.ProviderName;
}