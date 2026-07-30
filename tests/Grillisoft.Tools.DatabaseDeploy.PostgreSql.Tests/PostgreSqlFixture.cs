using Grillisoft.Tools.DatabaseDeploy.Tests;
using Grillisoft.Tools.DatabaseDeploy.Tests.Databases;
using Testcontainers.PostgreSql;

namespace Grillisoft.Tools.DatabaseDeploy.PostgreSql.Tests;

// ReSharper disable once ClassNeverInstantiated.Global
public sealed class PostgreSqlFixture : DatabaseFixture<PostgreSqlContainer>
{
    protected override PostgreSqlContainer CreateContainer() =>
        new PostgreSqlBuilder(ContainerImages.PostgreSql).Build();
}
