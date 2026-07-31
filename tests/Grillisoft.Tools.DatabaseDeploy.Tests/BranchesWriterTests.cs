using System.IO.Abstractions.TestingHelpers;

namespace Grillisoft.Tools.DatabaseDeploy.Tests;

public class BranchesWriterTests
{
    [Test]
    public async Task Release_MovesTheStepsToTheDefaultBranchAndDropsTheBranchFile()
    {
        //arrange
        var filesystem = SampleBranches.CreateFileSystem();
        var branches = await SampleBranches.Read(filesystem);
        var branch = branches.GetBranch("release/1.1");
        var sut = SampleBranches.CreateWriter(filesystem);

        //act
        var released = await sut.Release(branch.Steps, branches.GetBranchFiles(branch));

        //assert
        released.Should().BeEquivalentTo(["release/1.1"]);

        SampleBranches.ReadFile(filesystem, "main.csv").Should().Be(
            "Database01,_Init\nDatabase02,_Init\nDatabase01,TKT-001.SampleDescription\n");

        filesystem.File.Exists($"{SampleBranches.RootPath}release_1.1.csv").Should().BeFalse();

        //the '@include' of the released branch is dropped, every other line is left as it was
        SampleBranches.ReadFile(filesystem, "release_1.2.csv").Should().Be("Database02,TKT-002.SampleDescription\n");
        SampleBranches.ReadFile(filesystem, "release_1.3.csv").Should().Be("# a comment\nDatabase01,TKT-003.SampleDescription\n");
    }

    [Test]
    public async Task Release_WhenTheBranchIncludesAnother_ReleasesBothBranches()
    {
        //arrange
        var filesystem = SampleBranches.CreateFileSystem();
        var branches = await SampleBranches.Read(filesystem);
        var branch = branches.GetBranch("release/1.2");
        var sut = SampleBranches.CreateWriter(filesystem);

        //act
        var released = await sut.Release(branch.Steps, branches.GetBranchFiles(branch));

        //assert
        released.Should().BeEquivalentTo(["release/1.2", "release/1.1"]);

        SampleBranches.ReadFile(filesystem, "main.csv").Should().Be(
            "Database01,_Init\nDatabase02,_Init\nDatabase01,TKT-001.SampleDescription\nDatabase02,TKT-002.SampleDescription\n");

        filesystem.File.Exists($"{SampleBranches.RootPath}release_1.1.csv").Should().BeFalse();
        filesystem.File.Exists($"{SampleBranches.RootPath}release_1.2.csv").Should().BeFalse();
        SampleBranches.ReadFile(filesystem, "release_1.3.csv").Should().Be("# a comment\nDatabase01,TKT-003.SampleDescription\n");
    }

    [Test]
    public async Task Release_WhenTheDefaultBranchUsesCrLf_KeepsTheLineEndings()
    {
        //arrange
        var filesystem = SampleBranches.CreateFileSystem("Database01,_Init\r\nDatabase02,_Init\r\n");
        var branches = await SampleBranches.Read(filesystem);
        var branch = branches.GetBranch("release/1.1");
        var sut = SampleBranches.CreateWriter(filesystem);

        //act
        await sut.Release(branch.Steps, branches.GetBranchFiles(branch));

        //assert
        SampleBranches.ReadFile(filesystem, "main.csv").Should().Be(
            "Database01,_Init\r\nDatabase02,_Init\r\nDatabase01,TKT-001.SampleDescription\r\n");
    }

    [Test]
    public async Task Release_WhenGivenTheDefaultBranch_Throws()
    {
        //arrange
        var filesystem = SampleBranches.CreateFileSystem();
        var branches = await SampleBranches.Read(filesystem);
        var branch = branches.GetBranch(SampleBranches.GlobalSettings.DefaultBranch);
        var sut = SampleBranches.CreateWriter(filesystem);

        //act
        var act = async () => await sut.Release(branch.Steps, branches.GetBranchFiles(branch));

        //assert
        await act.Should().ThrowAsync<ArgumentException>();
        SampleBranches.ReadFile(filesystem, "main.csv").Should().Be("Database01,_Init\nDatabase02,_Init\n");
        filesystem.File.Exists($"{SampleBranches.RootPath}main.csv").Should().BeTrue();
    }
}
