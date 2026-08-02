using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Options;
using Grillisoft.Tools.DatabaseDeploy.Services;
using Grillisoft.Tools.DatabaseDeploy.Tests.Mocks;
using Microsoft.Extensions.DependencyInjection;

namespace Grillisoft.Tools.DatabaseDeploy.Tests.Services;

public class RollbackServiceHooksTests
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

    public RollbackServiceHooksTests()
    {
        _cancellationToken = _cancellationTokenSource.Token;
    }

    [Test]
    public async Task Execute_WhenHooksAreConfigured_RunsThemAroundTheRollbackScripts()
    {
        //arrange
        var (database01, database02) = await CreateDeployedDatabases();
        var sut = CreateService(CreateOptions("release/1.1"), database01, database02);

        //act
        var result = await sut.Execute(_cancellationToken);

        //assert
        result.Should().Be(0);

        //no script of its own for the rollback hooks: both come from the root folder
        database01.Scripts.Should().BeEquivalentTo([
            SampleFilesystems.Hooks.SharedPreRollbackScript,
            "TKT-001.SampleDescription.Rollback.sql",
            SampleFilesystems.Hooks.SharedPostRollbackScript
        ], options => options.WithStrictOrdering());

        //Database02 has nothing to rollback on this branch
        database02.Scripts.Should().BeEmpty();
    }

    [Test]
    public async Task Execute_WhenDryRun_RunsNoHook()
    {
        //arrange
        var (database01, database02) = await CreateDeployedDatabases();
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
    public async Task Execute_WhenThePreRollbackScriptFails_RollsBackNothing()
    {
        //arrange
        var (database01, database02) = await CreateDeployedDatabases();
        database01.FailingScripts.Add(SampleFilesystems.Hooks.SharedPreRollbackScript);
        var sut = CreateService(CreateOptions("release/1.1"), database01, database02);

        //act
        var act = async () => await sut.Execute(_cancellationToken);

        //assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        database01.Scripts.Should().BeEmpty();
        (await database01.GetMigrations(_cancellationToken)).Count.Should().Be(2);
    }

    [Test]
    public async Task Execute_WhenThePostRollbackScriptFails_CompletesAndReturnsTheFailuresCount()
    {
        //arrange
        var (database01, database02) = await CreateDeployedDatabases();
        database01.FailingScripts.Add(SampleFilesystems.Hooks.SharedPostRollbackScript);
        var sut = CreateService(CreateOptions("release/1.1"), database01, database02);

        //act
        var result = await sut.Execute(_cancellationToken);

        //assert
        result.Should().Be(1);

        //the rollback itself is done
        var migrations = await database01.GetMigrations(_cancellationToken);
        migrations.Count.Should().Be(1);
        migrations.First().Name.Should().Be(_globalSettings.InitStepName);
    }

    /// <summary>
    /// Database01 with the init step and TKT-001 deployed, Database02 with the init step only.
    /// </summary>
    private async Task<(DatabaseMock, DatabaseMock)> CreateDeployedDatabases()
    {
        var database01 = new DatabaseMock("Database01");
        await database01.AddMigration(new DatabaseMigration(_globalSettings.InitStepName, "user", TestHash), _cancellationToken);
        await database01.AddMigration(new DatabaseMigration("TKT-001.SampleDescription", "user", TestHash), _cancellationToken);

        var database02 = new DatabaseMock("Database02");
        await database02.AddMigration(new DatabaseMigration(_globalSettings.InitStepName, "user", TestHash), _cancellationToken);

        return (database01, database02);
    }

    private static RollbackOptions CreateOptions(string branch) => new()
    {
        Path = SampleFilesystems.Sample01RootPath,
        Branch = branch
    };

    private RollbackService CreateService(RollbackOptions rollbackOptions, params IDatabase[] databases)
    {
        var collection = new DatabasesCollectionMock(
            _fileSystem.DirectoryInfo.New(SampleFilesystems.Sample01RootPath),
            databases);
        foreach (var database in databases)
            collection.Hooks.Add(database.Name, AllHooks);

        var provider = new TestServiceCollection<RollbackService>()
            .AddSingleton(rollbackOptions)
            .AddRootDirectory(rollbackOptions.Path)
            .AddSingleton<IFileSystem>(_fileSystem)
            .AddSingleton<IProgress<int>>(new Progress<int>())
            .AddSingleton<IDatabaseFactory>(new DatabaseFactoryMock(databases))
            .AddSingleton<IDatabasesCollection>(collection)
            .Configure<GlobalSettings>(options => { })
            .BuildServiceProvider();

        return provider.GetRequiredService<RollbackService>();
    }
}
