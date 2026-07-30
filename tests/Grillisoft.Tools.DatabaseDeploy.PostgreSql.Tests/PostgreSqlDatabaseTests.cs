using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Tests.Databases;

namespace Grillisoft.Tools.DatabaseDeploy.PostgreSql.Tests;

[InheritsTests]
[ClassDataSource<PostgreSqlFixture>(Shared = SharedType.PerClass)]
public class PostgreSqlDatabaseTests : DatabaseTest<PostgreSqlDatabase>
{
    public PostgreSqlDatabaseTests(PostgreSqlFixture fixture)
        : base(fixture)
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
