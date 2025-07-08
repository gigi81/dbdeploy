using System.IO.Abstractions.TestingHelpers;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Options;
using Grillisoft.Tools.DatabaseDeploy.Services;
using Microsoft.Extensions.Logging;
using Moq;
using ExtensionsOptions = Microsoft.Extensions.Options.Options;

namespace Grillisoft.Tools.DatabaseDeploy.Tests.Services;

public class ValidateServiceTests
{
    private readonly ValidateOptions _options;
    private readonly Mock<IDatabasesCollection> _databases;
    private readonly Mock<ILogger> _logger;

    public ValidateServiceTests()
    {
        _options = new ValidateOptions { Path = "/path" };
        _databases = new Mock<IDatabasesCollection>();
        _logger = new Mock<ILogger>();
    }

    [Fact]
    public async Task Execute_WhenValidationSucceeds_ReturnsZero()
    {
        // Arrange
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile("/path/main.csv", new MockFileData("MyDb,01_init"));
        fileSystem.AddFile("/path/MyDb/01_init.sql", new MockFileData("SELECT 1"));

        var globalSettings = ExtensionsOptions.Create(
            new GlobalSettings { DefaultBranch = "main", InitStepName = "01_init" });
        var service = new ValidateService(_options, _databases.Object, fileSystem, globalSettings, _logger.Object);

        // Act
        var result = await service.Execute(CancellationToken.None);

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task Execute_WhenValidationFailsWithConfigurationErrors_ReturnsErrorCount()
    {
        // Arrange
        var fileSystem = new MockFileSystem();
        fileSystem.AddDirectory("/path/MyDb");
        fileSystem.AddFile("/path/main.csv", new MockFileData("MyDb,01_init"));

        var globalSettings = ExtensionsOptions.Create(new GlobalSettings());
        var service = new ValidateService(_options, _databases.Object, fileSystem, globalSettings, _logger.Object);

        // Act
        var result = await service.Execute(CancellationToken.None);

        // Assert
        Assert.True(result > 0);
    }

    [Fact]
    public async Task Execute_WhenValidationFailsWithUnexpectedError_ReturnsMinusOne()
    {
        // Arrange
        var fileSystem = new MockFileSystem();
        var globalSettings = ExtensionsOptions.Create(new GlobalSettings());
        var service = new ValidateService(_options, _databases.Object, fileSystem, globalSettings, _logger.Object);

        // Act
        var result = await service.Execute(CancellationToken.None);

        // Assert
        Assert.Equal(-1, result);
    }
}
