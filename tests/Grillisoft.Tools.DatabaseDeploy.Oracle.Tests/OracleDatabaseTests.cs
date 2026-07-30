using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Tests.Databases;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle.Tests;

[InheritsTests]
[ClassDataSource<OracleFixture>(Shared = SharedType.PerClass)]
public class OracleDatabaseTests : DatabaseTest<OracleDatabase>
{
    public OracleDatabaseTests(OracleFixture fixture)
        : base(fixture)
    {
    }

    protected override IDictionary<string, string?> GetConfigurationSettings()
    {
        var ret = base.GetConfigurationSettings();
        ret.Add("databases:test:schema", "oracle");
        ret.Add("databases:test:migrationTable", "MIGRATIONS");
        return ret;
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
