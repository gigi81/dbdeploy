using Divergic.Logging.Xunit;
using Grillisoft.Tools.DatabaseDeploy.Tests.Databases;
using Microsoft.Extensions.Logging;
using Testcontainers.MySql;
using Xunit.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.MySql.Tests;

public class MySqlDatabaseTests : DatabaseTest<MySqlDatabase, MySqlContainer>
{
    private readonly ILogger<MySqlDatabase> _logger;

    public MySqlDatabaseTests(ITestOutputHelper output)
        : base(new MySqlBuilder().Build())
    {
        _logger = output.BuildLoggerFor<MySqlDatabase>();
    }

    protected override MySqlDatabase CreateDatabase()
    {
        return new MySqlDatabase(
            "test",
            this.ConnectionString,
            "__Migrations",
            new MySqlScriptParser(),
            _logger);
    }
}
