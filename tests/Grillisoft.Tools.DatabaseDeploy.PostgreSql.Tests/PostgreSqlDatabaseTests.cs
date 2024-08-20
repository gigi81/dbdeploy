using Grillisoft.Tools.DatabaseDeploy.Tests.Databases;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using Xunit.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.PostgreSql.Tests;

public class PostgreSqlDatabaseTests : DatabaseTest<PostgreSqlDatabase, PostgreSqlContainer>
{
    private readonly ILogger<PostgreSqlDatabase> _logger;

    public PostgreSqlDatabaseTests(ITestOutputHelper output)
        : base(new PostgreSqlBuilder().Build())
    {
        _logger = output.BuildLoggerFor<PostgreSqlDatabase>();
    }

    protected override PostgreSqlDatabase CreateDatabase()
    {
        _logger.LogInformation(this.ConnectionString);

        return new PostgreSqlDatabase(
            "test",
            this.ConnectionString,
            "__Migrations",
            60,
            new PostgreSqlScriptParser(),
            _logger);
    }
}