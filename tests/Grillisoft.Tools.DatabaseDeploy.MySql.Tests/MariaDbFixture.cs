using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using Grillisoft.Tools.DatabaseDeploy.Tests;
using Grillisoft.Tools.DatabaseDeploy.Tests.Databases;
using Testcontainers.MySql;

namespace Grillisoft.Tools.DatabaseDeploy.MySql.Tests;

/// <summary>
/// A MariaDB server, driven through the MySQL builder because the two speak the same wire protocol
/// and the provider makes no distinction between them.
/// </summary>
/// <remarks>
/// This is the second container of this test project, which
/// <see cref="DatabaseFixture{TContainer}"/> is otherwise at pains to avoid. It earns its place:
/// the integration job in CI runs against MariaDB, and the parts of DDL generation MariaDB does
/// differently - sequences, packages, the missing dependency views - would otherwise ship with no
/// test at all.
/// </remarks>
// ReSharper disable once ClassNeverInstantiated.Global
public sealed class MariaDbFixture : DatabaseFixture<MySqlContainer>
{
    protected override MySqlContainer CreateContainer() =>
        new MySqlBuilder(ContainerImages.MariaDb)
            .WithCommand(MySqlServerOptions.TrustFunctionCreators)
            .WithWaitStrategy(WaitUntilReady)
            .Build();

    /// <summary>
    /// The MySQL module waits by running <c>mysqladmin</c>, which MariaDB 11 no longer ships even
    /// as a symlink, so the default strategy never completes and the run hangs on a container that
    /// has been up for minutes. <c>mariadb-admin</c> is its replacement, and answering it over TCP
    /// rather than the socket is what says the real server is up: the temporary one the entrypoint
    /// runs during initialisation does not listen on the network.
    /// </summary>
    private static IWaitForContainerOS WaitUntilReady => Wait.ForUnixContainer()
        .UntilCommandIsCompleted("mariadb-admin", "ping", "-h", "127.0.0.1", "--silent");
}
