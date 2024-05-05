using Grillisoft.Tools.DatabaseDeploy.Tests.Databases;
using Microsoft.Extensions.Logging;
using Testcontainers.Oracle;
using Xunit.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle.Tests;

public class OracleDatabaseTests : DatabaseTest<OracleDatabase, OracleContainer>
{
    private readonly ILogger<OracleDatabase> _logger;

    public OracleDatabaseTests(ITestOutputHelper output)
        : base(new OracleBuilder().Build())
    {
        _logger = output.BuildLoggerFor<OracleDatabase>();
    }

    protected override OracleDatabase CreateDatabase()
    {
        _logger.LogInformation(this.ConnectionString);
        
        return new OracleDatabase(
            "oracle", //NOTE: this is the same as the connection string
            this.ConnectionString,
            "Migrations",
            60,
            new OracleScriptParser(),
            _logger);
    }
}
