using System.IO.Abstractions.TestingHelpers;
using System.Runtime.InteropServices;
using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy.Tests.Services;

public static class SampleFilesystems
{
    private static bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static string Root = IsWindows ? "c:\\demo\\" : "/opt/demo/";
    private static string Database01 = IsWindows ? "c:\\demo\\Database01\\" : "/opt/demo/database01/";
    private static string Database02 = IsWindows ? "c:\\demo\\Database02\\" : "/opt/demo/database02/";
    
    public static MockFileSystem Sample01 = new(new Dictionary<string, MockFileData>
    {
        { $"{Root}main.csv", new MockFileData($@"
                Database01,{Step.InitStepName}
                Database02,{Step.InitStepName}
            ")},
        { $"{Root}release_1.1.csv", new MockFileData(@"
                Database01,TKT-001.SampleDescription
            ")},
        { $"{Root}release_1.2.csv", new MockFileData(@"
                @include release/1.1
                Database02,TKT-002.SampleDescription
            ")},
        { $"{Database01}{Step.InitStepName}.sql", new MockFileData("INIT Database01") },
        { $"{Database01}TKT-001.SampleDescription.Deploy.sql", new MockFileData("TKT-001.SampleDescription.Deploy.sql") },
        { $"{Database01}TKT-001.SampleDescription.Rollback.sql", new MockFileData("TKT-001.SampleDescription.Rollback.sql") },
        { $"{Database02}{Step.InitStepName}.sql", new MockFileData("INIT Database02") },
        { $"{Database02}TKT-002.SampleDescription.Deploy.sql", new MockFileData("TKT-002.SampleDescription.Deploy.sql") },
        { $"{Database02}TKT-002.SampleDescription.Rollback.sql", new MockFileData("TKT-002.SampleDescription.Rollback.sql") },
    });
}