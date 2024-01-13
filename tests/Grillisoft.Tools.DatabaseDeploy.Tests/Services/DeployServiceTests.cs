using System.IO.Abstractions.TestingHelpers;
using FluentAssertions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Options;
using Grillisoft.Tools.DatabaseDeploy.Services;
using Grillisoft.Tools.DatabaseDeploy.Tests.Mocks;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.Tests.Services;

public class DeployServiceTests
{
    private readonly ILogger<DeployService> _logger;
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    public DeployServiceTests(ITestOutputHelper output)
    {
        _logger = output.BuildLoggerFor<DeployService>();
    }
    
    [Fact]
    public async Task Execute_WhenDeployingMainBranch_IsSuccessful()
    {
        //arrange
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            { @"c:\demo\main.csv", new MockFileData($@"
                Database01,{Step.InitStepName}
                Database02,{Step.InitStepName}
            ")},
            { @"c:\demo\release_1.1.csv", new MockFileData(@"
                Database01,TKT-001.SampleDescription
            ")},
            { @"c:\demo\release_1.2.csv", new MockFileData(@"
                Database02,TKT-002.SampleDescription
            ")},
            { $@"c:\demo\Database01\{Step.InitStepName}.sql", new MockFileData("INIT Database01") },
            { @"c:\demo\Database01\TKT-001.SampleDescription.Deploy.sql", new MockFileData("TKT-001.SampleDescription.Deploy.sql") },
            { @"c:\demo\Database01\TKT-001.SampleDescription.Rollback.sql", new MockFileData("TKT-001.SampleDescription.Rollback.sql") },
            { $@"c:\demo\Database02\{Step.InitStepName}.sql", new MockFileData("INIT Database02") },
            { @"c:\demo\Database02\TKT-002.SampleDescription.Deploy.sql", new MockFileData("TKT-002.SampleDescription.Deploy.sql") },
            { @"c:\demo\Database02\TKT-002.SampleDescription.Rollback.sql", new MockFileData("TKT-002.SampleDescription.Rollback.sql") },
        });

        var options = new DeployOptions
        {
            Path = @"c:\demo"
        };
        
        var database01 = new DatabaseMock("Database01");
        var database02 = new DatabaseMock("Database02");
        var databaseFactory = new DatabaseFactoryMock(database01, database02);
        var cancellationToken = _cancellationTokenSource.Token;
        var sut = new DeployService(options, fileSystem, new []{ databaseFactory }, _logger);

        //act
        await sut.Execute(cancellationToken);

        //assert
        var migrations01 = await database01.GetMigrations(cancellationToken);
        var migrations02 = await database02.GetMigrations(cancellationToken);

        migrations01.Count.Should().Be(1);
        migrations02.Count.Should().Be(1);
        migrations01.First().Name.Should().Be(Step.InitStepName);
        migrations02.First().Name.Should().Be(Step.InitStepName);
    }
}