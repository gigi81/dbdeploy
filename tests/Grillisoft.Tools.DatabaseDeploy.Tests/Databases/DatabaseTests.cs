using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ExtensionsOptions = Microsoft.Extensions.Options.Options;

namespace Grillisoft.Tools.DatabaseDeploy.Tests.Databases;

/// <summary>
/// The contract every provider has to honour against a real database. A derived class supplies the
/// factory and points at a <see cref="DatabaseFixture{TContainer}"/> with
/// <c>[ClassDataSource&lt;T&gt;(Shared = SharedType.PerClass)]</c>, and marks itself
/// <c>[InheritsTests]</c> so the cases below run for it.
/// </summary>
/// <remarks>
/// The cases all clear and re-create the one migrations table of the one shared database, so they
/// cannot run alongside each other: <c>[NotInParallel]</c> is what buys back the isolation that a
/// container per test used to provide. Providers still run concurrently - each test project is its
/// own process.
/// </remarks>
[NotInParallel]
public abstract class DatabaseTest<TDatabase>
    where TDatabase : IDatabase
{
    private static readonly DatabaseMigration TestMigration =
        new("test", "user", "12345678123456781234567812345678");

    private readonly IDatabaseFixture _fixture;
    private readonly Lazy<IDatabaseFactory> _databaseFactory;
    private readonly Lazy<IConfiguration> _configuration;
    private readonly Lazy<IOptions<GlobalSettings>> _globalSettings;

    protected DatabaseTest(IDatabaseFixture fixture)
    {
        _fixture = fixture;
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
            return ExtensionsOptions.Create(section.Get<GlobalSettings>()!);
        });
    }

    protected ILogger<TDatabase> Logger => TestLogger<TDatabase>.Instance;

    protected ILoggerFactory LoggerFactory => TestLoggerFactory.Instance;

    protected IOptions<GlobalSettings> GlobalSettingsOptions => _globalSettings.Value;

    protected IConfiguration Configuration => _configuration.Value;

    protected string ContainerConnectionString => _fixture.ConnectionString;

    protected virtual string ConnectionString => _fixture.ConnectionString;

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

    protected async Task<TDatabase> CreateDatabase(CancellationToken cancellationToken)
    {
        var config = _configuration.Value.GetSection("databases:test");
        return (TDatabase)await _databaseFactory.Value.GetDatabase("test", config, cancellationToken);
    }

    [Test]
    [Category(TestCategories.Docker)]
    public async Task InitializeMigrations_Then_GetMigrations_ShouldBeEmpty(CancellationToken cancellationToken)
    {
        //arrange
        var sut = await this.CreateDatabase(cancellationToken);

        //act
        await sut.ClearMigrations(cancellationToken);
        await sut.InitializeMigrations(cancellationToken);
        var migrations = await sut.GetMigrations(cancellationToken);

        //assert
        migrations.Count.Should().Be(0);
    }

    [Test]
    [Category(TestCategories.Docker)]
    public async Task ClearMigrations_ClearThenInitializeThenClearMigrations(CancellationToken cancellationToken)
    {
        //arrange
        var sut = await this.CreateDatabase(cancellationToken);

        //act
        await sut.ClearMigrations(cancellationToken);
        await sut.InitializeMigrations(cancellationToken);
        await sut.ClearMigrations(cancellationToken);
    }

    [Test]
    [Category(TestCategories.Docker)]
    public async Task AddMigrations_InitializeThenAddMigration_ShouldHaveOneMigration(CancellationToken cancellationToken)
    {
        //arrange
        var sut = await this.CreateDatabase(cancellationToken);

        //act
        await sut.ClearMigrations(cancellationToken);
        await sut.InitializeMigrations(cancellationToken);
        await sut.AddMigration(TestMigration, cancellationToken);
        var migrations = await sut.GetMigrations(cancellationToken);

        //assert
        migrations.Count.Should().Be(1);
        migrations.First().Should().BeEquivalentTo(TestMigration);
    }

    [Test]
    [Category(TestCategories.Docker)]
    public async Task RemoveMigrations_Then_InitializeThenAddThenRemoveMigration_ShouldBeEmpty(CancellationToken cancellationToken)
    {
        //arrange
        var sut = await this.CreateDatabase(cancellationToken);

        //act
        await sut.ClearMigrations(cancellationToken);
        await sut.InitializeMigrations(cancellationToken);
        await sut.AddMigration(TestMigration, cancellationToken);
        var migrationsBefore = await sut.GetMigrations(cancellationToken);
        await sut.RemoveMigration(TestMigration, cancellationToken);
        var migrationsAfter = await sut.GetMigrations(cancellationToken);

        //assert
        migrationsBefore.Count.Should().Be(1);
        migrationsBefore.First().Should().BeEquivalentTo(TestMigration);
        migrationsAfter.Count.Should().Be(0);
    }

    [Test]
    [Category(TestCategories.Docker)]
    public async Task Exists_ShouldReturnTrue(CancellationToken cancellationToken)
    {
        //arrange
        var sut = await this.CreateDatabase(cancellationToken);

        //act
        var exists = await sut.Exists(cancellationToken);

        //assert
        exists.Should().Be(true);
    }
}
