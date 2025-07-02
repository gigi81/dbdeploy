using DotNet.Testcontainers.Containers;
using FluentAssertions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.Tests.Databases;

public abstract class DatabaseTest<TDatabase, TDatabaseContainer> : IAsyncLifetime
    where TDatabase : IDatabase
    where TDatabaseContainer : DockerContainer, IDatabaseContainer
{
    private static readonly DatabaseMigration TestMigration =
        new("test", "user", "12345678123456781234567812345678");

    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly CancellationToken _cancellationToken;
    private readonly TDatabaseContainer _container;
    private readonly ILogger<TDatabase> _logger;
    private readonly Lazy<IDatabaseFactory> _databaseFactory;
    private readonly Lazy<IConfiguration> _configuration;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Lazy<IOptions<GlobalSettings>> _globalSettings;

    protected DatabaseTest(TDatabaseContainer container, ITestOutputHelper output)
    {
        _container = container;
        _cancellationToken = _cancellationTokenSource.Token;
        _logger = output.BuildLoggerFor<TDatabase>();
        _loggerFactory = output.BuildLoggerFactory();
        _databaseFactory = new Lazy<IDatabaseFactory>(this.CreateDatabaseFactory);
        _configuration = new Lazy<IConfiguration>(() =>
        {
            // ReSharper disable once ConvertToLambdaExpression
            return new ConfigurationManager()
                .AddInMemoryCollection(this.GetConfigurationSettings())
                .Build();
        });
        _globalSettings = new Lazy<IOptions<GlobalSettings>>(() =>
        {
            var section = _configuration.Value.GetSection(GlobalSettings.SectionName);
            return Microsoft.Extensions.Options.Options.Create(section.Get<GlobalSettings>()!);
        });
    }

    protected ILogger<TDatabase> Logger => _logger;

    protected ILoggerFactory LoggerFactory => _loggerFactory;

    protected IOptions<GlobalSettings> GlobalSettingsOptions => _globalSettings.Value;

    protected IConfiguration Configuration => _configuration.Value;

    protected string ConnectionString => _container.GetConnectionString();

    protected abstract IDatabaseFactory CreateDatabaseFactory();

    protected abstract string ProviderName { get; }

    protected virtual IDictionary<string, string?> GetConfigurationSettings()
    {
        return new Dictionary<string, string?>()
        {
            { "global:scriptTimeout", "600" },
            { "databases:test:connectionString", this.ConnectionString },
            { "databases:test:provider", this.ProviderName }
        };
    }

    private async Task<TDatabase> CreateDatabase()
    {
        var config = _configuration.Value.GetSection("databases:test");
        return (TDatabase)await _databaseFactory.Value.GetDatabase("test", config, _cancellationToken);
    }

    [Fact]
    [Trait(nameof(DockerPlatform), nameof(DockerPlatform.Linux))]
    public async Task InitializeMigrations_Then_GetMigrations_ShouldBeEmpty()
    {
        //arrange
        var sut = await this.CreateDatabase();

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
        var sut = await this.CreateDatabase();

        //act
        await sut.ClearMigrations(_cancellationToken);
        await sut.InitializeMigrations(_cancellationToken);
        await sut.ClearMigrations(_cancellationToken);
    }

    [Fact]
    [Trait(nameof(DockerPlatform), nameof(DockerPlatform.Linux))]
    public async Task AddMigrations_InitializeThenAddMigration_ShouldHaveOneMigration()
    {
        //arrange
        var sut = await this.CreateDatabase();

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
        var sut = await this.CreateDatabase();

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
        var sut = await this.CreateDatabase();

        //act
        var exists = await sut.Exists(_cancellationToken);

        //assert
        exists.Should().Be(true);
    }

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}