using System.IO.Abstractions.TestingHelpers;
using FakeItEasy;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
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
            { @"c:\myfile.txt", new MockFileData("Testing is meh.") },
            { @"c:\demo\jQuery.js", new MockFileData("some js") },
            { @"c:\demo\image.gif", new MockFileData(new byte[] { 0x12, 0x34, 0x56, 0xd2 }) }
        });

        var options = new DeployOptions
        {
            Path = @"c:\demo"
        };

        var databaseFactory = A.Fake<IDatabaseFactory>();
        var database = new DatabaseMock(new ScriptParserMock());
        
        A.CallTo(() => databaseFactory.GetDatabase("", _cancellationTokenSource.Token)).Returns(database);
        
        var logger = A.Fake<ILogger<DeployService>>();
        
        var sut = new DeployService(options, fileSystem, new []{ databaseFactory }, logger);

        //act
        await sut.Execute(_cancellationTokenSource.Token);

        //assert
        
    }
}