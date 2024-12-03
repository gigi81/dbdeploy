using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Tests.Databases;
using Testcontainers.Oracle;
using Xunit.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle.Tests;

public class OracleDatabaseTests : DatabaseTest<OracleDatabase, OracleContainer>
{
    public OracleDatabaseTests(ITestOutputHelper output)
        : base(new OracleBuilder().Build(), output)
    {
    }

    protected override IDatabaseFactory CreateDatabaseFactory()
    {
        return new OracleDatabaseFactory(
            new OracleScriptParser(),
            this.GlobalSettingsOptions,
            this.LoggerFactory);
    }

    protected override string ProviderName => OracleDatabaseFactory.ProviderName;
}
