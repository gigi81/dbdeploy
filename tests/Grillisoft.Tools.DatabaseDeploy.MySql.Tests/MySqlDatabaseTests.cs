using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Tests.Databases;
using Testcontainers.MySql;
using Xunit.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.MySql.Tests;

public class MySqlDatabaseTests : DatabaseTest<MySqlDatabase, MySqlContainer>
{
    public MySqlDatabaseTests(ITestOutputHelper output)
        : base(new MySqlBuilder().Build(), output)
    {
    }

    protected override IDatabaseFactory CreateDatabaseFactory()
    {
        return new MySqlDatabaseFactory(
            new MySqlScriptParser(),
            this.GlobalSettingsOptions,
            this.LoggerFactory);
    }

    protected override string ProviderName => MySqlDatabaseFactory.ProviderName;
}
