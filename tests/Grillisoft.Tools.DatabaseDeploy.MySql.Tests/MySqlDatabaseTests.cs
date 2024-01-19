using Grillisoft.Tools.DatabaseDeploy.Tests.Databases;
using Testcontainers.MySql;

namespace Grillisoft.Tools.DatabaseDeploy.MySql.Tests;

public class MySqlDatabaseTests : DatabaseTest<MySqlDatabase, MySqlContainer>
{
    public MySqlDatabaseTests()
        : base(new MySqlBuilder().Build())
    {
    }

    protected override MySqlDatabase CreateDatabase()
    {
        return new MySqlDatabase(
            "test",
            this.ConnectionString,
            "__Migrations",
            new MySqlScriptParser());
    }
}
