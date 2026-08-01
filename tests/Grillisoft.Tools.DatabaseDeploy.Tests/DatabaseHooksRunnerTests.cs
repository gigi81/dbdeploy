using System.IO.Abstractions.TestingHelpers;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Exceptions;
using Grillisoft.Tools.DatabaseDeploy.Tests.Mocks;

namespace Grillisoft.Tools.DatabaseDeploy.Tests;

public class DatabaseHooksRunnerTests
{
    private const string HookName = "_PreDeploy";
    private const string DatabaseScript = "PRE DEPLOY of Database01";
    private const string RootScript = "PRE DEPLOY of everything";

    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly CancellationToken _cancellationToken;
    private readonly MockFileSystem _fileSystem = new();

    public DatabaseHooksRunnerTests()
    {
        _cancellationToken = _cancellationTokenSource.Token;
    }

    [Test]
    public async Task Run_WhenTheDatabaseHasItsOwnScript_RunsItInsteadOfTheSharedOne()
    {
        //arrange
        AddScript($"Database01{Path.DirectorySeparatorChar}{HookName}.sql", DatabaseScript);
        AddScript($"{HookName}.sql", RootScript);
        var database = new DatabaseMock("Database01");
        var sut = CreateRunner(database);

        //act
        await sut.Run(DatabaseHook.PreDeploy, ["Database01"], _cancellationToken);

        //assert
        database.Scripts.Should().BeEquivalentTo([DatabaseScript]);
    }

    [Test]
    public async Task Run_WhenTheDatabaseHasNoScriptOfItsOwn_RunsTheSharedOne()
    {
        //arrange
        AddScript($"{HookName}.sql", RootScript);
        var database = new DatabaseMock("Database01");
        var sut = CreateRunner(database);

        //act
        await sut.Run(DatabaseHook.PreDeploy, ["Database01"], _cancellationToken);

        //assert
        database.Scripts.Should().BeEquivalentTo([RootScript]);
    }

    [Test]
    public async Task Run_WhenTheHookIsNotConfigured_RunsNothing()
    {
        //arrange
        AddScript($"{HookName}.sql", RootScript);
        var database = new DatabaseMock("Database01");
        var sut = CreateRunner(DatabaseHooks.None, database);

        //act
        await sut.Run(DatabaseHook.PreDeploy, ["Database01"], _cancellationToken);

        //assert
        database.Scripts.Should().BeEmpty();
    }

    [Test]
    public async Task Run_WhenDryRun_RunsNothing()
    {
        //arrange
        AddScript($"{HookName}.sql", RootScript);
        var database = new DatabaseMock("Database01");
        var sut = CreateRunner(dryRun: true, database);

        //act
        await sut.Run(DatabaseHook.PreDeploy, ["Database01"], _cancellationToken);

        //assert
        database.Scripts.Should().BeEmpty();
    }

    [Test]
    public async Task Run_WhenTheScriptIsMissing_Throws()
    {
        //arrange
        var database = new DatabaseMock("Database01");
        var sut = CreateRunner(database);

        //act
        var act = async () => await sut.Run(DatabaseHook.PreDeploy, ["Database01"], _cancellationToken);

        //assert
        await act.Should().ThrowExactlyAsync<HookScriptNotFoundException>();
    }

    [Test]
    public async Task Run_WhenTheScriptFails_StopsAtTheFirstFailure()
    {
        //arrange
        AddScript($"{HookName}.sql", RootScript);
        var database01 = new DatabaseMock("Database01");
        database01.FailingScripts.Add(RootScript);
        var database02 = new DatabaseMock("Database02");
        var sut = CreateRunner(database01, database02);

        //act
        var act = async () => await sut.Run(DatabaseHook.PreDeploy, ["Database01", "Database02"], _cancellationToken);

        //assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        database02.Scripts.Should().BeEmpty();
    }

    [Test]
    public async Task TryRun_WhenTheScriptFails_CarriesOnAndCountsTheFailures()
    {
        //arrange
        AddScript($"{HookName}.sql", RootScript);
        var database01 = new DatabaseMock("Database01");
        database01.FailingScripts.Add(RootScript);
        var database02 = new DatabaseMock("Database02");
        var sut = CreateRunner(database01, database02);

        //act
        var failed = await sut.TryRun(DatabaseHook.PreDeploy, ["Database01", "Database02"], _cancellationToken);

        //assert
        failed.Should().Be(1);
        database02.Scripts.Should().BeEquivalentTo([RootScript]);
    }

    [Test]
    public async Task TryRun_WhenEveryScriptRuns_ReturnsNoFailure()
    {
        //arrange
        AddScript($"{HookName}.sql", RootScript);
        var sut = CreateRunner(new DatabaseMock("Database01"), new DatabaseMock("Database02"));

        //act
        var failed = await sut.TryRun(DatabaseHook.PreDeploy, ["Database01", "Database02"], _cancellationToken);

        //assert
        failed.Should().Be(0);
    }

    private static string RootPath => OperatingSystem.IsWindows() ? "c:\\demo\\" : "/opt/demo/";

    private static readonly DatabaseHooks PreDeployOnly =
        new(HookName, string.Empty, string.Empty, string.Empty);

    private void AddScript(string relativePath, string content)
    {
        _fileSystem.AddFile($"{RootPath}{relativePath}", new MockFileData(content));
    }

    private DatabaseHooksRunner CreateRunner(params DatabaseMock[] databases)
    {
        return CreateRunner(PreDeployOnly, false, databases);
    }

    private DatabaseHooksRunner CreateRunner(bool dryRun, params DatabaseMock[] databases)
    {
        return CreateRunner(PreDeployOnly, dryRun, databases);
    }

    private DatabaseHooksRunner CreateRunner(DatabaseHooks hooks, params DatabaseMock[] databases)
    {
        return CreateRunner(hooks, false, databases);
    }

    private DatabaseHooksRunner CreateRunner(DatabaseHooks hooks, bool dryRun, DatabaseMock[] databases)
    {
        var collection = new DatabasesCollectionMock(databases.Cast<IDatabase>().ToArray());
        foreach (var database in databases)
            collection.Hooks.Add(database.Name, hooks);

        return new DatabaseHooksRunner(
            collection,
            _fileSystem.DirectoryInfo.New(RootPath),
            dryRun,
            TestLogger<DatabaseHooksRunner>.Instance);
    }
}
