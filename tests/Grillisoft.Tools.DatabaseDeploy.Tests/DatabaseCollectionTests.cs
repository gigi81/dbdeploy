using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Exceptions;
using Microsoft.Extensions.Configuration;
using Moq;

namespace Grillisoft.Tools.DatabaseDeploy.Tests;

public class DatabaseCollectionTests
{
    private const string FactoryProviderName = "provider01";

    private static IConfiguration CreateConfig(Dictionary<string, string?> settings)
    {
        var configurationBuilder = new ConfigurationBuilder();
        configurationBuilder.AddInMemoryCollection(settings);
        return configurationBuilder.Build();
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

    private static Mock<IDatabaseFactory> GetDatabaseFactory()
    {
        var factory = new Mock<IDatabaseFactory>();
        factory.Setup(f => f.Name).Returns(FactoryProviderName);
        return factory;
    }
}