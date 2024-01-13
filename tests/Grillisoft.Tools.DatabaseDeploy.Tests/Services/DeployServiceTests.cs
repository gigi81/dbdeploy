using System.IO.Abstractions.TestingHelpers;
using FakeItEasy;
using Grillisoft.Tools.DatabaseDeploy.Options;
using Grillisoft.Tools.DatabaseDeploy.Services;
using Grillisoft.Tools.DatabaseDeploy.Tests.Mocks;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.Tests.Services;

public class DeployServiceTests
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    
    [Fact]
    public async Task Test1()
    {
        //arrange
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            { @"c:\demo\main.csv", new MockFileData(@"
                Database01,.Init
                Database02,.Init
            ")},
            { @"c:\demo\release_1.csv", new MockFileData(@"
                Database01,TKT-001.SampleDescription
            ")},
            { @"c:\demo\release_2.csv", new MockFileData(@"
                Database02,TKT-002.SampleDescription
            ")},
            { @"c:\demo\Database01\.Init.sql", new MockFileData(".Init.sql") },
            { @"c:\demo\Database01\TKT-001.SampleDescription.Deploy.sql", new MockFileData("TKT-001.SampleDescription.Deploy.sql") },
            { @"c:\demo\Database01\TKT-001.SampleDescription.Rollback.sql", new MockFileData("TKT-001.SampleDescription.Rollback.sql") },
            { @"c:\demo\Database02\.Init.sql", new MockFileData(".Init.sql") },
            { @"c:\demo\Database02\TKT-002.SampleDescription.Deploy.sql", new MockFileData("TKT-002.SampleDescription.Deploy.sql") },
            { @"c:\demo\Database02\TKT-002.SampleDescription.Rollback.sql", new MockFileData("TKT-002.SampleDescription.Rollback.sql") },
        });

        var options = new DeployOptions
        {
            Path = @"c:\demo"
        };
        
        var database01 = new DatabaseMock();
        var database02 = new DatabaseMock();
        var databaseFactory = new DatabaseFactoryMock();
        databaseFactory.AddDatabase("Database01", database01);
        databaseFactory.AddDatabase("Database02", database02);
        var logger = A.Fake<ILogger<DeployService>>();
        
        var sut = new DeployService(options, fileSystem, new []{ databaseFactory }, logger);

        //act
        await sut.Execute(_cancellationTokenSource.Token);

        //assert
        
    }
}