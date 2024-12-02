using FluentAssertions;
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
    
    [Fact]
    public async Task GetDatabase_WhenProviderSpecified_ReturnsDatabase()
    {
        //arrange
        var cts = new CancellationTokenSource();
        var configuration = CreateConfig(new Dictionary<string, string?>()
        {
            { "databases:test:connectionString", "test" },
            { "databases:test:provider", FactoryProviderName }
        });
        
        var factory = new Mock<IDatabaseFactory>();
        factory.Setup(f => f.Name).Returns(FactoryProviderName);
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

    [Fact]
    public async Task GetDatabase_WhenDefaultProvider_ReturnsDatabase()
    {
        //arrange
        var cts = new CancellationTokenSource();
        var configuration = CreateConfig(new Dictionary<string, string?>()
        {
            { "databases:test:connectionString", "test" },
            { "global:defaultProvider", FactoryProviderName }
        });
        
        var factory = new Mock<IDatabaseFactory>();
        factory.Setup(f => f.Name).Returns(FactoryProviderName);
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

    [Fact]
    public async Task GetDatabase_WhenProviderMissing_Throws()
    {
        //arrange
        var cts = new CancellationTokenSource();
        var configuration = CreateConfig(new Dictionary<string, string?>()
        {
            { "databases:test:connectionString", "test" }
        });
        
        var factory = new Mock<IDatabaseFactory>();
        factory.Setup(f => f.Name).Returns(FactoryProviderName);

        await using var collection = new DatabasesCollection([factory.Object], configuration);
        
        //act
        var ex = await Record.ExceptionAsync(() => collection.GetDatabase("test", cts.Token));

        //assert
        Assert.True(ex != null, "The DatabaseProviderNotFoundException exception was not thrown.");
        ex.GetType().Should().Be(typeof(DatabaseProviderNotFoundException));
    }
}