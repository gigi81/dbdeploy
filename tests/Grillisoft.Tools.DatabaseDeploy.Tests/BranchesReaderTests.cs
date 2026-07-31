namespace Grillisoft.Tools.DatabaseDeploy.Tests;

public class BranchesReaderTests
{
    [Test]
    public async Task Load_ReadsEveryBranchOfTheDirectory()
    {
        //act
        var sut = await SampleBranches.Read(SampleBranches.CreateFileSystem());

        //assert
        sut.Branches.Keys.Should().BeEquivalentTo(["main", "release/1.1", "release/1.2", "release/1.3"]);
    }

    [Test]
    public async Task GetSteps_PrependsTheStepsOfTheDefaultBranch()
    {
        //arrange
        var sut = await SampleBranches.Read(SampleBranches.CreateFileSystem());

        //act
        var steps = sut.GetSteps(sut.GetBranch("release/1.2")).ToArray();

        //assert
        steps.Select(s => s.Name).Should().BeEquivalentTo(
        [
            "_Init",
            "_Init",
            "TKT-001.SampleDescription",
            "TKT-002.SampleDescription"
        ], options => options.WithStrictOrdering());
    }

    [Test]
    public async Task GetBranchFiles_ReturnsTheBranchFileAndTheOnesItIncludes()
    {
        //arrange
        var sut = await SampleBranches.Read(SampleBranches.CreateFileSystem());

        //act
        var files = sut.GetBranchFiles(sut.GetBranch("release/1.2"));

        //assert
        files.Select(f => f.Name).Should().BeEquivalentTo(["release_1.2.csv", "release_1.1.csv"]);
    }

    [Test]
    public async Task GetBranchFiles_WhenTheBranchIncludesNothing_ReturnsItsOwnFile()
    {
        //arrange
        var sut = await SampleBranches.Read(SampleBranches.CreateFileSystem());

        //act
        var files = sut.GetBranchFiles(sut.GetBranch("release/1.1"));

        //assert
        files.Select(f => f.Name).Should().BeEquivalentTo(["release_1.1.csv"]);
    }
}
