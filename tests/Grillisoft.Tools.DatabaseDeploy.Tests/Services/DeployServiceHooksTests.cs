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

public class DeployServiceHooksTests
{
    private static readonly string TestHash = new('0', Step.HashLength);

    private static readonly IDictionary<DatabaseHook, string> AllHooks = TestHooks.Of(
        (DatabaseHook.PreDeploy, SampleFilesystems.Hooks.PreDeploy),
        (DatabaseHook.PostDeploy, SampleFilesystems.Hooks.PostDeploy),
        (DatabaseHook.PreRollback, SampleFilesystems.Hooks.PreRollback),
        (DatabaseHook.PostRollback, SampleFilesystems.Hooks.PostRollback));

    private readonly GlobalSettings _globalSettings = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly CancellationToken _cancellationToken;
    private readonly MockFileSystem _fileSystem = SampleFilesystems.Sample02;

    public DeployServiceHooksTests()
    {
        _cancellationToken = _cancellationTokenSource.Token;
    }

    [Test]
    public async Task Execute_WhenHooksAreConfigured_RunsThemAroundTheStepScripts()
    {
        //arrange
        var database01 = new DatabaseMock("Database01");
        var database02 = new DatabaseMock("Database02");
        var sut = CreateService(CreateOptions("release/1.1"), database01, database02);

        //act
        var result = await sut.Execute(_cancellationToken);

        //assert
        result.Should().Be(0);

        //the database folder script wins over the shared one
        database01.Scripts.Should().BeEquivalentTo([
            SampleFilesystems.Hooks.Database01PreDeployScript,
            "INIT Database01",
            "TKT-001.SampleDescription.Deploy.sql",
            SampleFilesystems.Hooks.SharedPostDeployScript
        ], options => options.WithStrictOrdering());

        //Database02 has no script of its own, so it falls back to the one in the root folder
        database02.Scripts.Should().BeEquivalentTo([
            SampleFilesystems.Hooks.SharedPreDeployScript,
            "INIT Database02",
            SampleFilesystems.Hooks.SharedPostDeployScript
        ], options => options.WithStrictOrdering());
    }

    [Test]
    public async Task Execute_WhenADatabaseHasNothingToDeploy_RunsNoHookForIt()
    {
        //arrange
        var database01 = new DatabaseMock("Database01");
        var database02 = new DatabaseMock("Database02");
        await database02.AddMigration(new DatabaseMigration(_globalSettings.InitStepName, "user", TestHash), _cancellationToken);
        var sut = CreateService(CreateOptions("release/1.1"), database01, database02);

        //act
        await sut.Execute(_cancellationToken);

        //assert
        database01.Scripts.Should().Contain(SampleFilesystems.Hooks.Database01PreDeployScript);
        database02.Scripts.Should().BeEmpty();
    }

    [Test]
    public async Task Execute_WhenDryRun_RunsNoHook()
    {
        //arrange
        var database01 = new DatabaseMock("Database01");
        var database02 = new DatabaseMock("Database02");
        var options = CreateOptions("release/1.1");
        options.DryRun = true;
        var sut = CreateService(options, database01, database02);

        //act
        var result = await sut.Execute(_cancellationToken);

        //assert
        result.Should().Be(0);
        database01.Scripts.Should().BeEmpty();
        database02.Scripts.Should().BeEmpty();
    }

    [Test]
    public async Task Execute_WhenThePreDeployScriptFails_DeploysNothing()
    {
        //arrange
        var database01 = new DatabaseMock("Database01");
        database01.FailingScripts.Add(SampleFilesystems.Hooks.Database01PreDeployScript);
        var database02 = new DatabaseMock("Database02");
        var sut = CreateService(CreateOptions("release/1.1"), database01, database02);

        //act
        var act = async () => await sut.Execute(_cancellationToken);

        //assert
        await act.Should().ThrowAsync<InvalidOperationException>();

        database01.Scripts.Should().BeEmpty();
        database02.Scripts.Should().BeEmpty();
        (await database01.GetMigrations(_cancellationToken)).Should().BeEmpty();
        (await database02.GetMigrations(_cancellationToken)).Should().BeEmpty();
    }

    [Test]
    public async Task Execute_WhenThePostDeployScriptFails_CompletesAndReturnsTheFailuresCount()
    {
        //arrange
        var database01 = new DatabaseMock("Database01");
        database01.FailingScripts.Add(SampleFilesystems.Hooks.SharedPostDeployScript);
        var database02 = new DatabaseMock("Database02");
        var sut = CreateService(CreateOptions("release/1.1"), database01, database02);

        //act
        var result = await sut.Execute(_cancellationToken);

        //assert
        result.Should().Be(1);

        //the deployment itself is done and the other database still runs its post deploy script
        (await database01.GetMigrations(_cancellationToken)).Count.Should().Be(2);
        database02.Scripts.Should().Contain(SampleFilesystems.Hooks.SharedPostDeployScript);
    }

    [Test]
    public async Task Execute_WhenThePostDeployScriptFailsWithUpdate_StillMovesTheStepsToMain()
    {
        //arrange
        var database01 = new DatabaseMock("Database01");
        database01.FailingScripts.Add(SampleFilesystems.Hooks.SharedPostDeployScript);
        var options = CreateOptions("release/1.1");
        options.Update = true;
        var sut = CreateService(options, database01, new DatabaseMock("Database02"));

        //act
        var result = await sut.Execute(_cancellationToken);

        //assert
        result.Should().Be(1);
        _fileSystem.File.Exists($"{SampleFilesystems.Sample01RootPath}release_1.1.csv").Should().BeFalse();
    }

    [Test]
    public async Task Execute_WhenAHookScriptIsMissing_FailsBeforeDeployingAnything()
    {
        //arrange
        var database01 = new DatabaseMock("Database01");
        var database02 = new DatabaseMock("Database02");
        var hooks = AllHooks.With(DatabaseHook.PreDeploy, "_MissingPreDeploy");
        var sut = CreateService(CreateOptions("release/1.1"), hooks, database01, database02);

        //act
        var act = async () => await sut.Execute(_cancellationToken);

        //assert
        var exception = await act.Should().ThrowAsync<InvalidBranchesConfigurationException>();
        exception.Which.Errors.Should().Contain(e => e.Contains("_MissingPreDeploy.sql"));
        database01.Scripts.Should().BeEmpty();
        database02.Scripts.Should().BeEmpty();
    }

    private static DeployOptions CreateOptions(string branch) => new()
    {
        Path = SampleFilesystems.Sample01RootPath,
        Branch = branch
    };

    private DeployService CreateService(DeployOptions deployOptions, params IDatabase[] databases)
    {
        return CreateService(deployOptions, AllHooks, databases);
    }

    private DeployService CreateService(
        DeployOptions deployOptions,
        IDictionary<DatabaseHook, string> hooks,
        params IDatabase[] databases)
    {
        var collection = new DatabasesCollectionMock(
            _fileSystem.DirectoryInfo.New(SampleFilesystems.Sample01RootPath),
            databases);
        foreach (var database in databases)
            collection.Hooks.Add(database.Name, hooks);

        var provider = new TestServiceCollection<DeployService>()
            .AddSingleton(deployOptions)
            .AddRootDirectory(deployOptions.Path)
            .AddSingleton<IFileSystem>(_fileSystem)
            .AddSingleton<IProgress<int>>(new Progress<int>())
            .AddSingleton<IDatabaseFactory>(new DatabaseFactoryMock(databases))
            .AddSingleton<IDatabasesCollection>(collection)
            .Configure<GlobalSettings>(options => { })
            .BuildServiceProvider();

        return provider.GetRequiredService<DeployService>();
    }
}
