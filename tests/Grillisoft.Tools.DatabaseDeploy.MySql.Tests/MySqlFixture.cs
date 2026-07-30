using Grillisoft.Tools.DatabaseDeploy.Tests;
using Grillisoft.Tools.DatabaseDeploy.Tests.Databases;
using Testcontainers.MySql;

namespace Grillisoft.Tools.DatabaseDeploy.MySql.Tests;

// ReSharper disable once ClassNeverInstantiated.Global
public sealed class MySqlFixture : DatabaseFixture<MySqlContainer>
{
    protected override MySqlContainer CreateContainer() =>
        new MySqlBuilder(ContainerImages.MySql).Build();
}
