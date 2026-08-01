using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Exceptions;
using Grillisoft.Tools.DatabaseDeploy.Options;
using Grillisoft.Tools.DatabaseDeploy.Services;
using Grillisoft.Tools.DatabaseDeploy.Tests.Mocks;
using Microsoft.Extensions.DependencyInjection;

namespace Grillisoft.Tools.DatabaseDeploy.Tests.Services;

public class DeployServiceTests
{
    private readonly GlobalSettings _globalSettings = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly CancellationToken _cancellationToken;
    private readonly MockFileSystem _fileSystem = SampleFilesystems.Sample01;

    public DeployServiceTests()
    {
        _cancellationToken = _cancellationTokenSource.Token;
    }

    [Test]
    public async Task Execute_WhenDeployingMainBranch_IsSuccessful()
    {
        //arrange
        var database01 = new DatabaseMock("Database01");
        var database02 = new DatabaseMock("Database02");
        var deployOptions = new DeployOptions
        {
            Path = SampleFilesystems.Sample01RootPath
        };
        var sut = CreateService(deployOptions, database01, database02);

        //act
        await sut.Execute(_cancellationToken);

        //assert
        var migrations01 = await database01.GetMigrations(_cancellationToken);
        var migrations02 = await database02.GetMigrations(_cancellationToken);

        migrations01.Count.Should().Be(1);
        migrations02.Count.Should().Be(1);
        migrations01.First().Name.Should().Be(_globalSettings.InitStepName);
        migrations02.First().Name.Should().Be(_globalSettings.InitStepName);
    }

    [Test]
    public async Task Execute_WhenDeployingRelease1_1Branch_IsSuccessful()
    {
        //arrange
        var database01 = new DatabaseMock("Database01");
        var database02 = new DatabaseMock("Database02");
        var deployOptions = new DeployOptions
        {
            Path = SampleFilesystems.Sample01RootPath,
            Branch = "release/1.1"
        };
        var sut = CreateService(deployOptions, database01, database02);

        //act
        await sut.Execute(_cancellationToken);

        //assert
        var migrations01 = await database01.GetMigrations(_cancellationToken);
        var migrations02 = await database02.GetMigrations(_cancellationToken);

        migrations01.Count.Should().Be(2);
        migrations02.Count.Should().Be(1);
        migrations01.First().Name.Should().Be(_globalSettings.InitStepName);
        migrations01.Skip(1).First().Name.Should().Be("TKT-001.SampleDescription");
        migrations02.First().Name.Should().Be(_globalSettings.InitStepName);
    }

    [Test]
    public async Task Execute_WhenDeployingRelease1_2Branch_IsSuccessful()
    {
        //arrange
        var database01 = new DatabaseMock("Database01");
        var database02 = new DatabaseMock("Database02");
        var deployOptions = new DeployOptions
        {
            Path = SampleFilesystems.Sample01RootPath,
            Branch = "release/1.2"
        };
        var sut = CreateService(deployOptions, database01, database02);

        //act
        await sut.Execute(_cancellationToken);

        //assert
        var migrations01 = await database01.GetMigrations(_cancellationToken);
        var migrations02 = await database02.GetMigrations(_cancellationToken);

        migrations01.Count.Should().Be(2);
        migrations01.First().Name.Should().Be(_globalSettings.InitStepName);
        migrations01.Skip(1).First().Name.Should().Be("TKT-001.SampleDescription");

        migrations02.Count.Should().Be(2);
        migrations02.First().Name.Should().Be(_globalSettings.InitStepName);
        migrations02.Skip(1).First().Name.Should().Be("TKT-002.SampleDescription");
    }

    [Test]
    public async Task Execute_WhenDryRun_RunsNoScriptAndWritesNothing()
    {
        //arrange
        var database01 = new DatabaseMock("Database01");
        var database02 = new DatabaseMock("Database02");
        var deployOptions = new DeployOptions
        {
            Path = SampleFilesystems.Sample01RootPath,
            Branch = "release/1.1",
            Test = true,
            DryRun = true
        };
        var sut = CreateService(deployOptions, database01, database02);

        //act
        var result = await sut.Execute(_cancellationToken);

        //assert
        result.Should().Be(0);
        database01.Scripts.Should().BeEmpty();
        database02.Scripts.Should().BeEmpty();
        database01.IsMigrationsTableInitialized.Should().BeFalse();
        database02.IsMigrationsTableInitialized.Should().BeFalse();

        var migrations01 = await database01.GetMigrations(_cancellationToken);
        var migrations02 = await database02.GetMigrations(_cancellationToken);
        migrations01.Should().BeEmpty();
        migrations02.Should().BeEmpty();
    }

    [Test]
    public async Task Execute_WhenDryRunAndTheDatabaseIsMissing_DoesNotCreateIt()
    {
        //arrange
        var database01 = new DatabaseMock("Database01") { IsExisting = false };
        var database02 = new DatabaseMock("Database02");
        var deployOptions = new DeployOptions
        {
            Path = SampleFilesystems.Sample01RootPath,
            Create = true,
            DryRun = true
        };
        var sut = CreateService(deployOptions, database01, database02);

        //act
        var act = async () => await sut.Execute(_cancellationToken);

        //assert
        await act.Should().ThrowAsync<DatabasesNotFoundException>();
        database01.IsCreated.Should().BeFalse();
    }

    [Test]
    public async Task Execute_WhenTheDatabaseIsMissingAndCreateIsSet_CreatesIt()
    {
        //arrange
        var database01 = new DatabaseMock("Database01") { IsExisting = false };
        var database02 = new DatabaseMock("Database02");
        var deployOptions = new DeployOptions
        {
            Path = SampleFilesystems.Sample01RootPath,
            Create = true
        };
        var sut = CreateService(deployOptions, database01, database02);

        //act
        await sut.Execute(_cancellationToken);

        //assert
        database01.IsCreated.Should().BeTrue();
    }

    [Test]
    public async Task Execute_WhenDeployingRelease1_1BranchWithUpdate_MovesTheStepsToMain()
    {
        //arrange
        var deployOptions = new DeployOptions
        {
            Path = SampleFilesystems.Sample01RootPath,
            Branch = "release/1.1",
            Update = true
        };
        var sut = CreateService(deployOptions, new DatabaseMock("Database01"), new DatabaseMock("Database02"));

        //act
        await sut.Execute(_cancellationToken);

        //assert
        ReadBranchFile("main").Should().BeEquivalentTo(
        [
            $"Database01,{_globalSettings.InitStepName}",
            $"Database02,{_globalSettings.InitStepName}",
            "Database01,TKT-001.SampleDescription"
        ], options => options.WithStrictOrdering());

        _fileSystem.File.Exists($"{SampleFilesystems.Sample01RootPath}release_1.1.csv").Should().BeFalse();

        //the include of the released branch is gone, the steps of release/1.2 are untouched
        ReadBranchFile("release_1.2").Should().BeEquivalentTo(
            ["Database02,TKT-002.SampleDescription"], options => options.WithStrictOrdering());

        //the layout that is left is still valid
        var errors = await new BranchesReader(
            _fileSystem.DirectoryInfo.New(SampleFilesystems.Sample01RootPath),
            _globalSettings,
            SampleBranches.NoHooks).Load();
        errors.Should().BeEmpty();
    }

    [Test]
    public async Task Execute_WhenDeployingRelease1_2BranchWithUpdate_MovesTheIncludedBranchToo()
    {
        //arrange
        var deployOptions = new DeployOptions
        {
            Path = SampleFilesystems.Sample01RootPath,
            Branch = "release/1.2",
            Update = true
        };
        var sut = CreateService(deployOptions, new DatabaseMock("Database01"), new DatabaseMock("Database02"));

        //act
        await sut.Execute(_cancellationToken);

        //assert
        ReadBranchFile("main").Should().BeEquivalentTo(
        [
            $"Database01,{_globalSettings.InitStepName}",
            $"Database02,{_globalSettings.InitStepName}",
            "Database01,TKT-001.SampleDescription",
            "Database02,TKT-002.SampleDescription"
        ], options => options.WithStrictOrdering());

        _fileSystem.File.Exists($"{SampleFilesystems.Sample01RootPath}release_1.1.csv").Should().BeFalse();
        _fileSystem.File.Exists($"{SampleFilesystems.Sample01RootPath}release_1.2.csv").Should().BeFalse();
    }

    [Test]
    public async Task Execute_WhenDeployingWithUpdateAndDryRun_LeavesTheBranchFilesUntouched()
    {
        //arrange
        var deployOptions = new DeployOptions
        {
            Path = SampleFilesystems.Sample01RootPath,
            Branch = "release/1.1",
            Update = true,
            DryRun = true
        };
        var sut = CreateService(deployOptions, new DatabaseMock("Database01"), new DatabaseMock("Database02"));

        //act
        await sut.Execute(_cancellationToken);

        //assert
        ReadBranchFile("main").Should().BeEquivalentTo(
        [
            $"Database01,{_globalSettings.InitStepName}",
            $"Database02,{_globalSettings.InitStepName}"
        ], options => options.WithStrictOrdering());

        _fileSystem.File.Exists($"{SampleFilesystems.Sample01RootPath}release_1.1.csv").Should().BeTrue();
        ReadBranchFile("release_1.2").Should().BeEquivalentTo(
        [
            "@include release/1.1",
            "Database02,TKT-002.SampleDescription"
        ], options => options.WithStrictOrdering());
    }

    [Test]
    public async Task Execute_WhenDeployingMainBranchWithUpdate_LeavesTheBranchFilesUntouched()
    {
        //arrange
        var deployOptions = new DeployOptions
        {
            Path = SampleFilesystems.Sample01RootPath,
            Update = true
        };
        var sut = CreateService(deployOptions, new DatabaseMock("Database01"), new DatabaseMock("Database02"));

        //act
        await sut.Execute(_cancellationToken);

        //assert
        ReadBranchFile("main").Should().BeEquivalentTo(
        [
            $"Database01,{_globalSettings.InitStepName}",
            $"Database02,{_globalSettings.InitStepName}"
        ], options => options.WithStrictOrdering());

        _fileSystem.File.Exists($"{SampleFilesystems.Sample01RootPath}release_1.1.csv").Should().BeTrue();
        _fileSystem.File.Exists($"{SampleFilesystems.Sample01RootPath}release_1.2.csv").Should().BeTrue();
    }

    private string[] ReadBranchFile(string name)
    {
        return _fileSystem.File.ReadAllLines($"{SampleFilesystems.Sample01RootPath}{name}.csv")
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrEmpty(l))
            .ToArray();
    }

    private DeployService CreateService(DeployOptions deployOptions, params IDatabase[] databases)
    {
        var provider = new TestServiceCollection<DeployService>()
            .AddSingleton(deployOptions)
            .AddSingleton<IFileSystem>(_fileSystem)
            .AddSingleton<IProgress<int>>(new Progress<int>())
            .AddSingleton<IDatabaseFactory>(new DatabaseFactoryMock(databases))
            .AddSingleton<IDatabasesCollection>(new DatabasesCollectionMock(databases))
            .Configure<GlobalSettings>(options => { })
            .BuildServiceProvider();

        return provider.GetRequiredService<DeployService>();
    }
}