using System.IO.Abstractions.TestingHelpers;
using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy.Tests.Services;

public static class SampleFilesystems
{
    public static MockFileSystem Sample01 = new MockFileSystem(new Dictionary<string, MockFileData>
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
}