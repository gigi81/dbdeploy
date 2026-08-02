using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Options;
using Grillisoft.Tools.DatabaseDeploy.Services;
using Grillisoft.Tools.DatabaseDeploy.Tests.Mocks;
using Microsoft.Extensions.DependencyInjection;
using ExtensionsOptions = Microsoft.Extensions.Options.Options;

namespace Grillisoft.Tools.DatabaseDeploy.Tests.Services;

public class ValidateServiceTests
{
    [Test]
    public async Task Execute_WhenValidationSucceeds_ReturnsZero()
    {
        // Arrange
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile("/path/main.csv", new MockFileData("MyDb,01_init"));
        fileSystem.AddFile("/path/MyDb/01_init.sql", new MockFileData("SELECT 1"));

        var globalSettings = new GlobalSettings { DefaultBranch = "main", InitStepName = "01_init" };
        var service = CreateService(fileSystem, globalSettings);

        // Act
        var result = await service.Execute(CancellationToken.None);

        // Assert
        result.Should().Be(0);
    }

    [Test]
    public async Task Execute_WhenValidationFailsWithConfigurationErrors_ReturnsErrorCount()
    {
        // Arrange
        var fileSystem = new MockFileSystem();
        fileSystem.AddDirectory("/path/MyDb");
        fileSystem.AddFile("/path/main.csv", new MockFileData("MyDb,01_init"));

        var globalSettings = new GlobalSettings();
        var service = CreateService(fileSystem, globalSettings);

        // Act
        var result = await service.Execute(CancellationToken.None);

        // Assert
        result.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task Execute_WhenValidationFailsWithUnexpectedError_ReturnsMinusOne()
    {
        // Arrange
        var fileSystem = new MockFileSystem();
        var globalSettings = new GlobalSettings();
        var service = CreateService(fileSystem, globalSettings);

        // Act
        var result = await service.Execute(CancellationToken.None);

        // Assert
        result.Should().Be(-1);
    }

    private ValidateService CreateService(IFileSystem fileSystem, GlobalSettings globalSettings)
    {
        var provider = new TestServiceCollection<ValidateService>()
            .AddSingleton(new ValidateOptions { Path = "/path" })
            .AddRootDirectory("/path")
            .AddSingleton(fileSystem)
            .AddSingleton<IProgress<int>>(new Progress<int>())
            .AddSingleton<IDatabaseFactory>(new DatabaseFactoryMock())
            .AddSingleton<IDatabasesCollection>(new DatabasesCollectionMock())
            .AddSingleton(ExtensionsOptions.Create(globalSettings))
            .BuildServiceProvider();

        return provider.GetRequiredService<ValidateService>();
    }
}
