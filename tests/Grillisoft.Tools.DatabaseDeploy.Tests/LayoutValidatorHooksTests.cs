using System.IO.Abstractions.TestingHelpers;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Tests.Mocks;

namespace Grillisoft.Tools.DatabaseDeploy.Tests;

public class LayoutValidatorHooksTests
{
    private const string PreDeployName = "_PreDeploy";

    [Test]
    public async Task Load_WhenTheHookScriptIsInTheDatabaseFolder_ReportsNoError()
    {
        //arrange
        var fileSystem = SampleBranches.CreateFileSystem();
        fileSystem.AddFile(DatabaseFile("Database01"), new MockFileData("PRE DEPLOY"));

        //act
        var errors = await Load(fileSystem, "Database01");

        //assert
        errors.Should().BeEmpty();
    }

    [Test]
    public async Task Load_WhenTheHookScriptIsOnlyInTheRootFolder_ReportsNoError()
    {
        //arrange
        var fileSystem = SampleBranches.CreateFileSystem();
        fileSystem.AddFile(RootFile(), new MockFileData("PRE DEPLOY"));

        //act
        var errors = await Load(fileSystem, "Database01", "Database02");

        //assert
        errors.Should().BeEmpty();
    }

    [Test]
    public async Task Load_WhenTheHookScriptIsMissing_ReportsAnError()
    {
        //arrange
        var fileSystem = SampleBranches.CreateFileSystem();

        //act
        var errors = await Load(fileSystem, "Database01");

        //assert
        errors.Should().ContainSingle()
            .Which.Should().Contain("PreDeploy")
            .And.Contain("Database01")
            .And.Contain($"{PreDeployName}.sql");
    }

    [Test]
    public async Task Load_WhenTheHookScriptIsNotConfigured_ReportsItAsUntracked()
    {
        //arrange
        var fileSystem = SampleBranches.CreateFileSystem();
        fileSystem.AddFile(DatabaseFile("Database01"), new MockFileData("PRE DEPLOY"));

        //act
        var errors = await Load(fileSystem);

        //assert
        errors.Should().ContainSingle()
            .Which.Should().StartWith("Untracked file detected");
    }

    private static string DatabaseFile(string database) =>
        $"{SampleBranches.RootPath}{database}{Path.DirectorySeparatorChar}{PreDeployName}.sql";

    private static string RootFile() => $"{SampleBranches.RootPath}{PreDeployName}.sql";

    /// <summary>
    /// Validates the sample branches with a pre deploy script configured for the given databases.
    /// </summary>
    private static async Task<List<string>> Load(MockFileSystem fileSystem, params string[] databases)
    {
        var root = fileSystem.DirectoryInfo.New(SampleBranches.RootPath);
        var collection = new DatabasesCollectionMock(root);

        foreach (var database in databases)
            collection.Hooks.Add(database, TestHooks.Of((DatabaseHook.PreDeploy, PreDeployName)));

        var reader = new BranchesReader(root, SampleBranches.GlobalSettings);

        await reader.Load();

        return await SampleBranches.Validate(reader, collection);
    }
}
