using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Exceptions;
using Microsoft.Extensions.Configuration;
using Moq;

namespace Grillisoft.Tools.DatabaseDeploy.Tests;

public class DatabaseCollectionTests
{
    private const string FactoryProviderName = "provider01";

    /// <summary>
    /// Which hooks came back is read through the scripts they would run, so these tests need a
    /// folder to hang them off. Nothing is read from it: only the names matter here.
    /// </summary>
    private static readonly IDirectoryInfo Directory =
        new MockFileSystem().DirectoryInfo.New(SampleBranches.RootPath);

    private static IEnumerable<DatabaseHook> ConfiguredHooks(DatabaseHooks hooks) =>
        hooks.GetHookScripts("test", Directory).Select(script => script.Hook);

    private static DatabasesConfiguration CreateConfig(Dictionary<string, string?> settings)
    {
        var configurationBuilder = new ConfigurationBuilder();
        configurationBuilder.AddInMemoryCollection(settings);
        return new DatabasesConfiguration(configurationBuilder.Build());
    }

    [Test]
    public async Task GetDatabase_WhenProviderSpecified_ReturnsDatabase()
    {
        //arrange
        var cts = new CancellationTokenSource();
        var configuration = CreateConfig(new Dictionary<string, string?>()
        {
            { "databases:test:connectionString", "test" },
            { "databases:test:provider", FactoryProviderName }
        });

        var factory = GetDatabaseFactory();
        var database = Mock.Of<IDatabase>();
        factory.SetupSequence(f => f.GetDatabase("test", It.IsAny<IConfigurationSection>(), cts.Token))
            .ReturnsAsync(database)
            .Throws(new Exception("This was expected to be called only once as it is cached afterwards"));

        await using var collection = new DatabasesCollection([factory.Object], configuration);

        //act
        var actualDatabase01 = await collection.GetDatabase("test", cts.Token);
        var actualDatabase02 = await collection.GetDatabase("test", cts.Token);

        //assert
        actualDatabase01.Should().BeSameAs(database);
        actualDatabase02.Should().BeSameAs(database);
    }

    [Test]
    public async Task GetDatabase_WhenDefaultProvider_ReturnsDatabase()
    {
        //arrange
        var cts = new CancellationTokenSource();
        var configuration = CreateConfig(new Dictionary<string, string?>()
        {
            { "databases:test:connectionString", "test" },
            { "global:defaultProvider", FactoryProviderName }
        });

        var factory = GetDatabaseFactory();
        var database = Mock.Of<IDatabase>();
        factory.SetupSequence(f => f.GetDatabase("test", It.IsAny<IConfigurationSection>(), cts.Token))
            .ReturnsAsync(database)
            .Throws(new Exception("This was expected to be called only once as it is cached afterwards"));

        await using var collection = new DatabasesCollection([factory.Object], configuration);

        //act
        var actualDatabase01 = await collection.GetDatabase("test", cts.Token);
        var actualDatabase02 = await collection.GetDatabase("test", cts.Token);

        //assert
        actualDatabase01.Should().BeSameAs(database);
        actualDatabase02.Should().BeSameAs(database);
    }

    [Test]
    public async Task GetDatabase_WhenProviderMissing_Throws()
    {
        //arrange
        var cts = new CancellationTokenSource();
        var configuration = CreateConfig(new Dictionary<string, string?>()
        {
            { "databases:test:connectionString", "test" }
        });

        var factory = GetDatabaseFactory();
        await using var collection = new DatabasesCollection([factory.Object], configuration);

        //act
        var act = () => collection.GetDatabase("test", cts.Token);

        //assert
        await act.Should().ThrowExactlyAsync<DatabaseProviderNotFoundException>();
    }

    /// <summary>
    /// Formatting asks for a dialect, not for a database, so the factory must never be asked to
    /// build one - a database with no connection string still has a formatter.
    /// </summary>
    [Test]
    public async Task GetSqlFormatter_WhenProviderSpecified_ReturnsFormatterWithoutBuildingTheDatabase()
    {
        //arrange
        var configuration = CreateConfig(new Dictionary<string, string?>()
        {
            { "databases:test:provider", FactoryProviderName }
        });

        var formatter = Mock.Of<ISqlFormatter>();
        var factory = GetDatabaseFactory();
        factory.Setup(f => f.SqlFormatter).Returns(formatter);
        factory.Setup(f => f.GetDatabase(It.IsAny<string>(), It.IsAny<IConfigurationSection>(), It.IsAny<CancellationToken>()))
            .Throws(new Exception("Formatting must not build a database"));

        await using var collection = new DatabasesCollection([factory.Object], configuration);

        //act
        var actual = collection.GetSqlFormatter("test");

        //assert
        actual.Should().BeSameAs(formatter);
    }

    [Test]
    public async Task GetSqlFormatter_WhenProviderMissing_ReturnsNull()
    {
        //arrange
        var configuration = CreateConfig(new Dictionary<string, string?>()
        {
            { "databases:test:connectionString", "test" }
        });

        var factory = GetDatabaseFactory();
        await using var collection = new DatabasesCollection([factory.Object], configuration);

        //act
        var actual = collection.GetSqlFormatter("test");

        //assert
        actual.Should().BeNull();
    }

    [Test]
    public async Task GetSqlFormatter_WhenProviderUnknown_Throws()
    {
        //arrange
        var configuration = CreateConfig(new Dictionary<string, string?>()
        {
            { "databases:test:provider", "nosuchprovider" }
        });

        var factory = GetDatabaseFactory();
        await using var collection = new DatabasesCollection([factory.Object], configuration);

        //act
        var act = () => collection.GetSqlFormatter("test");

        //assert
        act.Should().ThrowExactly<DatabaseProviderNotFoundException>();
    }

    [Test]
    public async Task GetHooks_WhenSetGlobally_ReturnsTheGlobalNames()
    {
        //arrange
        var configuration = CreateConfig(new Dictionary<string, string?>()
        {
            { "global:preDeploy", "_PreDeploy" },
            { "global:postRollback", "_PostRollback" },
            { "databases:test:provider", FactoryProviderName }
        });

        await using var collection = new DatabasesCollection([GetDatabaseFactory().Object], configuration);

        //act
        var actual = collection.GetHooks("test");

        //assert
        actual.Hooks[DatabaseHook.PreDeploy].Should().Be("_PreDeploy");
        actual.Hooks[DatabaseHook.PostRollback].Should().Be("_PostRollback");
        actual.Hooks[DatabaseHook.PostDeploy].Should().BeEmpty();
        actual.Hooks[DatabaseHook.PreRollback].Should().BeEmpty();
        ConfiguredHooks(actual).Should().BeEquivalentTo([DatabaseHook.PreDeploy, DatabaseHook.PostRollback]);
    }

    [Test]
    public async Task GetHooks_WhenSetOnTheDatabase_OverridesTheGlobalNames()
    {
        //arrange
        var configuration = CreateConfig(new Dictionary<string, string?>()
        {
            { "global:preDeploy", "_PreDeploy" },
            { "global:postDeploy", "_PostDeploy" },
            { "databases:test:preDeploy", "_TestPreDeploy" },
            { "databases:test:provider", FactoryProviderName }
        });

        await using var collection = new DatabasesCollection([GetDatabaseFactory().Object], configuration);

        //act
        var actual = collection.GetHooks("test");

        //assert
        actual.Hooks[DatabaseHook.PreDeploy].Should().Be("_TestPreDeploy");
        actual.Hooks[DatabaseHook.PostDeploy].Should().Be("_PostDeploy");
    }

    /// <summary>
    /// A database that does not want a hook the global settings turned on says so with an empty
    /// name. Falling back to the global name here would run a script the database opted out of.
    /// </summary>
    [Test]
    public async Task GetHooks_WhenTheDatabaseSetsAnEmptyName_TurnsTheGlobalHookOff()
    {
        //arrange
        var configuration = CreateConfig(new Dictionary<string, string?>()
        {
            { "global:preDeploy", "_PreDeploy" },
            { "global:postDeploy", "_PostDeploy" },
            { "databases:test:preDeploy", "" },
            { "databases:test:provider", FactoryProviderName }
        });

        await using var collection = new DatabasesCollection([GetDatabaseFactory().Object], configuration);

        //act
        var actual = collection.GetHooks("test");

        //assert
        actual.Hooks[DatabaseHook.PreDeploy].Should().BeEmpty();
        actual.TryGetHookScript(DatabaseHook.PreDeploy, "test", Directory, out _).Should().BeFalse();

        //the hooks it said nothing about are untouched
        actual.Hooks[DatabaseHook.PostDeploy].Should().Be("_PostDeploy");
        ConfiguredHooks(actual).Should().BeEquivalentTo([DatabaseHook.PostDeploy]);
    }

    [Test]
    public async Task GetHooks_WhenNotConfigured_ReturnsNoHook()
    {
        //arrange
        var configuration = CreateConfig(new Dictionary<string, string?>()
        {
            { "databases:test:provider", FactoryProviderName }
        });

        await using var collection = new DatabasesCollection([GetDatabaseFactory().Object], configuration);

        //act
        var actual = collection.GetHooks("test");

        //assert
        actual.Hooks.Should().BeEquivalentTo(DatabaseHooks.None.Hooks);
        ConfiguredHooks(actual).Should().BeEmpty();
    }

    private static Mock<IDatabaseFactory> GetDatabaseFactory()
    {
        var factory = new Mock<IDatabaseFactory>();
        factory.Setup(f => f.Name).Returns(FactoryProviderName);
        return factory;
    }
}