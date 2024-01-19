using DotNet.Testcontainers.Containers;
using FluentAssertions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy.Tests.Databases;

public abstract class DatabaseTest<TDatabase, TDatabaseContainer> : IAsyncLifetime
    where TDatabase: IDatabase
    where TDatabaseContainer: DockerContainer, IDatabaseContainer
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly CancellationToken _cancellationToken;
    private readonly TDatabaseContainer _container;

    protected DatabaseTest(TDatabaseContainer container)
    {
        _container = container;
        _cancellationToken = _cancellationTokenSource.Token;
    }

    protected string ConnectionString => _container.GetConnectionString();
    
    protected abstract TDatabase CreateDatabase();
    
    [Fact]
    [Trait(nameof(DockerPlatform), nameof(DockerPlatform.Linux))]
    public async Task InitializeMigrations_Then_GetMigrations_ShouldBeEmpty()
    {
        //arrange
        var sut = this.CreateDatabase();

        //act
        await sut.InitializeMigrations(_cancellationToken);
        var migrations = await sut.GetMigrations(_cancellationToken);

        //assert
        migrations.Count.Should().Be(0);
    }

    [Fact]
    [Trait(nameof(DockerPlatform), nameof(DockerPlatform.Linux))]
    public async Task InitializeMigrations_Then_AddMigration_ShouldHaveOneMigration()
    {
        //arrange
        var sut = this.CreateDatabase();
        var expected = new DatabaseMigration("test", DateTimeOffset.Now, "user", "1234");

        //act
        await sut.InitializeMigrations(_cancellationToken);
        await sut.AddMigration(expected, _cancellationToken);
        var migrations = await sut.GetMigrations(_cancellationToken);

        //assert
        migrations.Count.Should().Be(1);
        migrations.First().Should().BeEquivalentTo(expected);
    }

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}