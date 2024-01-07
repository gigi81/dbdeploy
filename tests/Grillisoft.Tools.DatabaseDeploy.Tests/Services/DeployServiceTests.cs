using System.IO.Abstractions.TestingHelpers;

namespace Grillisoft.Tools.DatabaseDeploy.Tests.Services;

public class DeployServiceTests
{
    [Fact]
    public void Test1()
    {
        //arrange
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            { @"c:\myfile.txt", new MockFileData("Testing is meh.") },
            { @"c:\demo\jQuery.js", new MockFileData("some js") },
            { @"c:\demo\image.gif", new MockFileData(new byte[] { 0x12, 0x34, 0x56, 0xd2 }) }
        });
        
        //act
        
        //assert
    }
}