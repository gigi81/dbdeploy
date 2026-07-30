using Grillisoft.Tools.DatabaseDeploy.Tests;
using Grillisoft.Tools.DatabaseDeploy.Tests.Databases;
using Testcontainers.MsSql;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer.Tests;

// ReSharper disable once ClassNeverInstantiated.Global
public sealed class SqlServerFixture : DatabaseFixture<MsSqlContainer>
{
    protected override MsSqlContainer CreateContainer() =>
        new MsSqlBuilder(ContainerImages.SqlServer).Build();
}
