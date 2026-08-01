using System.IO.Abstractions.TestingHelpers;
using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy.Tests;

/// <summary>
/// The layout of a deployment folder against what its branch files declare. The hook scripts are
/// covered by <see cref="LayoutValidatorHooksTests"/> and the encoding by
/// <see cref="LayoutValidatorEncodingTests"/>.
/// </summary>
public class LayoutValidatorTests
{
    private const string Step = "TKT-001.SampleDescription";

    [Test]
    public async Task Validate_WhenTheLayoutMatchesTheBranchFiles_ReportsNoError()
    {
        var errors = await Validate(SampleBranches.CreateFileSystem());

        errors.Should().BeEmpty();
    }

    // ------------------------------------------------------- mandatory files

    [Test]
    public async Task Validate_WhenADeployScriptIsMissing_ReportsIt()
    {
        var fileSystem = SampleBranches.CreateFileSystem();
        fileSystem.File.Delete(Script($"{Step}.Deploy"));

        var errors = await Validate(fileSystem);

        errors.Should().ContainSingle()
            .Which.Should().StartWith("Could not find mandatory file").And.Contain($"{Step}.Deploy.sql");
    }

    [Test]
    public async Task Validate_WhenAnInitScriptIsMissing_ReportsIt()
    {
        var fileSystem = SampleBranches.CreateFileSystem();
        fileSystem.File.Delete(Script("_Init"));

        var errors = await Validate(fileSystem);

        errors.Should().ContainSingle()
            .Which.Should().StartWith("Could not find mandatory file").And.Contain("_Init.sql");
    }

    [Test]
    public async Task Validate_WhenARollbackScriptIsMissing_ReportsIt()
    {
        var fileSystem = SampleBranches.CreateFileSystem();
        fileSystem.File.Delete(Script($"{Step}.Rollback"));

        var errors = await Validate(fileSystem);

        errors.Should().ContainSingle()
            .Which.Should().StartWith("Could not find mandatory file").And.Contain($"{Step}.Rollback.sql");
    }

    /// <summary>An init step is a starting point, so there is nothing to roll it back to.</summary>
    [Test]
    public async Task Validate_ShouldNotAskForTheRollbackScriptOfAnInitStep()
    {
        var errors = await Validate(SampleBranches.CreateFileSystem());

        errors.Should().NotContainMatch("*_Init.Rollback.sql*");
    }

    [Test]
    public async Task Validate_WhenRollbackIsNotRequired_AMissingRollbackScriptIsNoError()
    {
        var fileSystem = SampleBranches.CreateFileSystem();
        fileSystem.File.Delete(Script($"{Step}.Rollback"));

        var errors = await Validate(fileSystem, new GlobalSettings { RollbackRequired = false });

        errors.Should().BeEmpty();
    }

    /// <summary>
    /// Not required is not the same as not allowed: a rollback script that is there is still a
    /// tracked file rather than an untracked one.
    /// </summary>
    [Test]
    public async Task Validate_WhenRollbackIsNotRequired_ARollbackScriptThatIsThereIsNoError()
    {
        var errors = await Validate(
            SampleBranches.CreateFileSystem(),
            new GlobalSettings { RollbackRequired = false });

        errors.Should().BeEmpty();
    }

    // ------------------------------------------------------- untracked files

    [Test]
    public async Task Validate_WhenAScriptNoBranchFileDeclares_ReportsItAsUntracked()
    {
        var fileSystem = SampleBranches.CreateFileSystem();
        fileSystem.AddFile(Script("TKT-999.Forgotten.Deploy"), new MockFileData("DEPLOY"));

        var errors = await Validate(fileSystem);

        errors.Should().ContainSingle()
            .Which.Should().StartWith("Untracked file detected").And.Contain("TKT-999.Forgotten.Deploy.sql");
    }

    /// <summary>The whole tree is swept, so a script tucked away in a folder of its own is found.</summary>
    [Test]
    public async Task Validate_WhenAScriptIsInASubFolder_ReportsItAsUntracked()
    {
        var fileSystem = SampleBranches.CreateFileSystem();
        var path = $"{SampleBranches.RootPath}scratch{Path.DirectorySeparatorChar}notes.sql";
        fileSystem.AddFile(path, new MockFileData("SELECT 1"));

        var errors = await Validate(fileSystem);

        errors.Should().ContainSingle().Which.Should().StartWith("Untracked file detected").And.Contain(path);
    }

    /// <summary>Test scripts are optional, so one being there says nothing is wrong.</summary>
    [Test]
    public async Task Validate_ATestScriptIsTracked()
    {
        var fileSystem = SampleBranches.CreateFileSystem();
        fileSystem.AddFile(Script($"{Step}.Test"), new MockFileData("TEST"));

        var errors = await Validate(fileSystem);

        errors.Should().BeEmpty();
    }

    /// <summary>Data scripts are optional too, and a step may have several of them.</summary>
    [Test]
    public async Task Validate_DataScriptsAreTracked()
    {
        var fileSystem = SampleBranches.CreateFileSystem();
        fileSystem.AddFile(Script($"{Step}.Data"), new MockFileData("DATA"));
        fileSystem.AddFile(Script($"{Step}.Data01"), new MockFileData("MORE DATA"));

        var errors = await Validate(fileSystem);

        errors.Should().BeEmpty();
    }

    // ------------------------------------------------------- duplicate steps

    [Test]
    public async Task Validate_WhenABranchListsAStepTwice_ReportsIt()
    {
        var fileSystem = SampleBranches.CreateFileSystem("Database01,_Init\nDatabase02,_Init\nDatabase01,_Init\n");

        var errors = await Validate(fileSystem);

        errors.Should().ContainSingle()
            .Which.Should().Contain("_Init").And.Contain("Database01").And.Contain("main")
            .And.Contain("more than once");
    }

    /// <summary>
    /// The same step name under two databases is two different scripts, which is the whole point of
    /// the folder per database.
    /// </summary>
    [Test]
    public async Task Validate_WhenTwoDatabasesShareAStepName_ReportsNoError()
    {
        var errors = await Validate(SampleBranches.CreateFileSystem());

        errors.Should().BeEmpty("both databases have an _Init step");
    }

    // ------------------------------------------------------- step names

    [Test]
    public async Task Validate_WhenAStepDoesNotMatchTheNamingConvention_ReportsIt()
    {
        var settings = new GlobalSettings { StepsNameRegex = "^SPRINT-" };

        var errors = await Validate(SampleBranches.CreateFileSystem(), settings);

        errors.Should().NotBeEmpty();
        errors.Should().AllSatisfy(error => error.Should().Contain("does not match expected naming convention"));
        errors.Should().Contain(error => error.Contains(Step));
    }

    /// <summary>The init step is named by the settings rather than by whoever wrote the step.</summary>
    [Test]
    public async Task Validate_ShouldNotHoldTheInitStepToTheNamingConvention()
    {
        var settings = new GlobalSettings { StepsNameRegex = "^TKT-" };

        var errors = await Validate(SampleBranches.CreateFileSystem(), settings);

        errors.Should().BeEmpty();
    }

    [Test]
    public async Task Validate_WhenThereIsNoNamingConvention_ChecksNoName()
    {
        var settings = new GlobalSettings { StepsNameRegex = string.Empty };

        var errors = await Validate(SampleBranches.CreateFileSystem(), settings);

        errors.Should().BeEmpty();
    }

    private static string Script(string name) =>
        $"{SampleBranches.RootPath}Database01{Path.DirectorySeparatorChar}{name}.sql";

    private static async Task<List<string>> Validate(MockFileSystem fileSystem, GlobalSettings? settings = null)
    {
        settings ??= SampleBranches.GlobalSettings;

        var reader = new BranchesReader(fileSystem.DirectoryInfo.New(SampleBranches.RootPath), settings);
        await reader.Load();

        return await LayoutValidator.Validate(reader, settings, SampleBranches.NoHooks);
    }
}
