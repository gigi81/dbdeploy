using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Tests.Databases;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer.Tests;

[InheritsTests]
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerAssembly)]
public class SqlServerDatabaseTests : DatabaseTest<SqlServerDatabase>
{
    public SqlServerDatabaseTests(SqlServerFixture fixture)
        : base(fixture)
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
