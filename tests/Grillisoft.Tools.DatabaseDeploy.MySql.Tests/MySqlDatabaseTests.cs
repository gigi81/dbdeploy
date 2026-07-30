using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Tests.Databases;

namespace Grillisoft.Tools.DatabaseDeploy.MySql.Tests;

[InheritsTests]
[ClassDataSource<MySqlFixture>(Shared = SharedType.PerAssembly)]
public class MySqlDatabaseTests : DatabaseTest<MySqlDatabase>
{
    public MySqlDatabaseTests(MySqlFixture fixture)
        : base(fixture)
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
