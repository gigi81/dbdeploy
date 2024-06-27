using DotNet.Testcontainers.Containers;
using FluentAssertions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy.Tests.Databases;

public abstract class DatabaseTest<TDatabase, TDatabaseContainer> : IAsyncLifetime
    where TDatabase: IDatabase
    where TDatabaseContainer: DockerContainer, IDatabaseContainer
{
    private static readonly DatabaseMigration TestMigration =
        new("test", "user", "12345678123456781234567812345678");
    
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
        await sut.ClearMigrations(_cancellationToken);
        await sut.InitializeMigrations(_cancellationToken);
        var migrations = await sut.GetMigrations(_cancellationToken);

        //assert
        migrations.Count.Should().Be(0);
    }

    [Fact]
    [Trait(nameof(DockerPlatform), nameof(DockerPlatform.Linux))]
    public async Task ClearMigrations_ClearThenInitializeThenClearMigrations()
    {
        //arrange
        var sut = this.CreateDatabase();

        //act
        await sut.ClearMigrations(_cancellationToken);
        await sut.InitializeMigrations(_cancellationToken);
        var migrationsBefore = await sut.GetMigrations(_cancellationToken);
        await sut.ClearMigrations(_cancellationToken);

        //assert
        migrationsBefore.Count.Should().Be(1);
        migrationsBefore.First().Should().BeEquivalentTo(TestMigration);
    }

    [Fact]
    [Trait(nameof(DockerPlatform), nameof(DockerPlatform.Linux))]
    public async Task AddMigrations_InitializeThenAddMigration_ShouldHaveOneMigration()
    {
        //arrange
        var sut = this.CreateDatabase();

        //act
        await sut.ClearMigrations(_cancellationToken);
        await sut.InitializeMigrations(_cancellationToken);
        await sut.AddMigration(TestMigration, _cancellationToken);
        var migrations = await sut.GetMigrations(_cancellationToken);

        //assert
        migrations.Count.Should().Be(1);
        migrations.First().Should().BeEquivalentTo(TestMigration);
    }

    [Fact]
    [Trait(nameof(DockerPlatform), nameof(DockerPlatform.Linux))]
    public async Task RemoveMigrations_Then_InitializeThenAddThenRemoveMigration_ShouldBeEmpty()
    {
        //arrange
        var sut = this.CreateDatabase();

        //act
        await sut.ClearMigrations(_cancellationToken);
        await sut.InitializeMigrations(_cancellationToken);
        await sut.AddMigration(TestMigration, _cancellationToken);
        var migrationsBefore = await sut.GetMigrations(_cancellationToken);
        await sut.RemoveMigration(TestMigration, _cancellationToken);
        var migrationsAfter = await sut.GetMigrations(_cancellationToken);

        //assert
        migrationsBefore.Count.Should().Be(1);
        migrationsBefore.First().Should().BeEquivalentTo(TestMigration);
        migrationsAfter.Count.Should().Be(0);
    }
    
    [Fact]
    [Trait(nameof(DockerPlatform), nameof(DockerPlatform.Linux))]
    public async Task Exists_ShouldReturnTrue()
    {
        //arrange
        var sut = this.CreateDatabase();

        //act
        var exists = await sut.Exists(_cancellationToken);

        //assert
        exists.Should().Be(true);
    }

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}