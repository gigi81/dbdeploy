using System.IO.Abstractions.TestingHelpers;
using System.Runtime.InteropServices;
using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy.Tests.Services;

public static class SampleFilesystems
{
    private static bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public static string Sample01RootPath = IsWindows ? "c:\\demo\\" : "/opt/demo/";
    private static string Sample01Database01Path = IsWindows ? "c:\\demo\\Database01\\" : "/opt/demo/Database01/";
    private static string Sample01Database02Path = IsWindows ? "c:\\demo\\Database02\\" : "/opt/demo/Database02/";
    
    public static MockFileSystem Sample01 = new(new Dictionary<string, MockFileData>
    {
        { $"{Sample01RootPath}main.csv", new MockFileData($@"
                Database01,{Step.InitStepName}
                Database02,{Step.InitStepName}
            ")},
        { $"{Sample01RootPath}release_1.1.csv", new MockFileData(@"
                Database01,TKT-001.SampleDescription
            ")},
        { $"{Sample01RootPath}release_1.2.csv", new MockFileData(@"
                @include release/1.1
                Database02,TKT-002.SampleDescription
            ")},
        { $"{Sample01Database01Path}{Step.InitStepName}.sql", new MockFileData("INIT Database01") },
        { $"{Sample01Database01Path}TKT-001.SampleDescription.Deploy.sql", new MockFileData("TKT-001.SampleDescription.Deploy.sql") },
        { $"{Sample01Database01Path}TKT-001.SampleDescription.Rollback.sql", new MockFileData("TKT-001.SampleDescription.Rollback.sql") },
        { $"{Sample01Database02Path}{Step.InitStepName}.sql", new MockFileData("INIT Database02") },
        { $"{Sample01Database02Path}TKT-002.SampleDescription.Deploy.sql", new MockFileData("TKT-002.SampleDescription.Deploy.sql") },
        { $"{Sample01Database02Path}TKT-002.SampleDescription.Rollback.sql", new MockFileData("TKT-002.SampleDescription.Rollback.sql") },
    });
}