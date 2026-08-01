using System.IO.Abstractions.TestingHelpers;
using System.Runtime.InteropServices;
using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy.Tests.Services;

public static class SampleFilesystems
{
    private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public static readonly string Sample01RootPath = IsWindows ? "c:\\demo\\" : "/opt/demo/";
    private static readonly string Sample01Database01Path = IsWindows ? "c:\\demo\\Database01\\" : "/opt/demo/Database01/";
    private static readonly string Sample01Database02Path = IsWindows ? "c:\\demo\\Database02\\" : "/opt/demo/Database02/";
    private static readonly GlobalSettings GlobalSettings = new();

    public static class Hooks
    {
        public const string PreDeploy = "_PreDeploy";
        public const string PostDeploy = "_PostDeploy";
        public const string PreRollback = "_PreRollback";
        public const string PostRollback = "_PostRollback";

        public const string Database01PreDeployScript = "Database01 PreDeploy";
        public const string SharedPreDeployScript = "Shared PreDeploy";
        public const string SharedPostDeployScript = "Shared PostDeploy";
        public const string SharedPreRollbackScript = "Shared PreRollback";
        public const string SharedPostRollbackScript = "Shared PostRollback";
    }

    /// <summary>
    /// A new file system on every access: the tests that deploy with --update write to it.
    /// </summary>
    public static MockFileSystem Sample01 => new(new Dictionary<string, MockFileData>
    {
        { $"{Sample01RootPath}main.csv", new MockFileData($@"
                Database01,{GlobalSettings.InitStepName}
                Database02,{GlobalSettings.InitStepName}
            ")},
        { $"{Sample01RootPath}release_1.1.csv", new MockFileData(@"
                Database01,TKT-001.SampleDescription
            ")},
        { $"{Sample01RootPath}release_1.2.csv", new MockFileData(@"
                @include release/1.1
                Database02,TKT-002.SampleDescription
            ")},
        { $"{Sample01Database01Path}{GlobalSettings.InitStepName}.sql", new MockFileData("INIT Database01") },
        { $"{Sample01Database01Path}TKT-001.SampleDescription.Deploy.sql", new MockFileData("TKT-001.SampleDescription.Deploy.sql") },
        { $"{Sample01Database01Path}TKT-001.SampleDescription.Rollback.sql", new MockFileData("TKT-001.SampleDescription.Rollback.sql") },
        { $"{Sample01Database02Path}{GlobalSettings.InitStepName}.sql", new MockFileData("INIT Database02") },
        { $"{Sample01Database02Path}TKT-002.SampleDescription.Deploy.sql", new MockFileData("TKT-002.SampleDescription.Deploy.sql") },
        { $"{Sample01Database02Path}TKT-002.SampleDescription.Rollback.sql", new MockFileData("TKT-002.SampleDescription.Rollback.sql") },
    });

    /// <summary>
    /// <see cref="Sample01"/> plus the hook scripts: Database01 has its own pre deploy script,
    /// every other hook is the shared one in the root folder.
    /// </summary>
    public static MockFileSystem Sample02
    {
        get
        {
            var fileSystem = Sample01;

            fileSystem.AddFile($"{Sample01Database01Path}{Hooks.PreDeploy}.sql", new MockFileData(Hooks.Database01PreDeployScript));
            fileSystem.AddFile($"{Sample01RootPath}{Hooks.PreDeploy}.sql", new MockFileData(Hooks.SharedPreDeployScript));
            fileSystem.AddFile($"{Sample01RootPath}{Hooks.PostDeploy}.sql", new MockFileData(Hooks.SharedPostDeployScript));
            fileSystem.AddFile($"{Sample01RootPath}{Hooks.PreRollback}.sql", new MockFileData(Hooks.SharedPreRollbackScript));
            fileSystem.AddFile($"{Sample01RootPath}{Hooks.PostRollback}.sql", new MockFileData(Hooks.SharedPostRollbackScript));

            return fileSystem;
        }
    }
}