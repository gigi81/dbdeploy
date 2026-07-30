using Grillisoft.Tools.DatabaseDeploy.Tests;
using Grillisoft.Tools.DatabaseDeploy.Tests.Databases;
using Testcontainers.Oracle;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle.Tests;

// ReSharper disable once ClassNeverInstantiated.Global
public sealed class OracleFixture : DatabaseFixture<OracleContainer>
{
    protected override OracleContainer CreateContainer() =>
        new OracleBuilder(ContainerImages.Oracle).Build();
}
