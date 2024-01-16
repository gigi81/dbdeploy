using FluentAssertions;
using Grillisoft.Tools.DatabaseDeploy.Tests;
using Testcontainers.MySql;
using Xunit;

namespace Grillisoft.Tools.DatabaseDeploy.MySql.Tests;

public class MySqlDatabaseTests : IAsyncLifetime
{
    private readonly MySqlContainer _container = new MySqlBuilder().Build();
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly CancellationToken _cancellationToken;

    public MySqlDatabaseTests()
    {
        _cancellationToken = _cancellationTokenSource.Token;
    }
    
    [Fact]
    [Trait(nameof(DockerPlatform), nameof(DockerPlatform.Linux))]
    public async Task InitializeMigrations_Then_GetMigrations_ShouldBeEmpty()
    {
        //arrange
        var sut = new MySqlDatabase("test", _container.GetConnectionString(), "__Migrations", new MySqlScriptParser());

        //act
        await sut.InitializeMigrations(_cancellationToken);
        var migrations = await sut.GetMigrations(_cancellationToken);

        //assert
        migrations.Count.Should().Be(0);
    }

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
