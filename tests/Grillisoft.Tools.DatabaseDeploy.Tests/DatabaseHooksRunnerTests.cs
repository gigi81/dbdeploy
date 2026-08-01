using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
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
        await sut.Run(DatabaseHook.PreDeploy, ["Database01"], Root, false, _cancellationToken);

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
        await sut.Run(DatabaseHook.PreDeploy, ["Database01"], Root, false, _cancellationToken);

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
        await sut.Run(DatabaseHook.PreDeploy, ["Database01"], Root, false, _cancellationToken);

        //assert
        database.Scripts.Should().BeEmpty();
    }

    [Test]
    public async Task Run_WhenDryRun_RunsNothing()
    {
        //arrange
        AddScript($"{HookName}.sql", RootScript);
        var database = new DatabaseMock("Database01");
        var sut = CreateRunner(database);

        //act
        await sut.Run(DatabaseHook.PreDeploy, ["Database01"], Root, true, _cancellationToken);

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
        var act = async () => await sut.Run(DatabaseHook.PreDeploy, ["Database01"], Root, false, _cancellationToken);

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
        var act = async () => await sut.Run(DatabaseHook.PreDeploy, ["Database01", "Database02"], Root, false, _cancellationToken);

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
        var failed = await sut.TryRun(DatabaseHook.PreDeploy, ["Database01", "Database02"], Root, false, _cancellationToken);

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
        var failed = await sut.TryRun(DatabaseHook.PreDeploy, ["Database01", "Database02"], Root, false, _cancellationToken);

        //assert
        failed.Should().Be(0);
    }

    private static string RootPath => OperatingSystem.IsWindows() ? "c:\\demo\\" : "/opt/demo/";

    private IDirectoryInfo Root => _fileSystem.DirectoryInfo.New(RootPath);

    private void AddScript(string relativePath, string content)
    {
        _fileSystem.AddFile($"{RootPath}{relativePath}", new MockFileData(content));
    }

    private DatabaseHooksRunner CreateRunner(params DatabaseMock[] databases)
    {
        return CreateRunner(new DatabaseHooks(HookName, string.Empty, string.Empty, string.Empty), databases);
    }

    private static DatabaseHooksRunner CreateRunner(DatabaseHooks hooks, params DatabaseMock[] databases)
    {
        var collection = new DatabasesCollectionMock(databases.Cast<Abstractions.IDatabase>().ToArray());
        foreach (var database in databases)
            collection.Hooks.Add(database.Name, hooks);

        return new DatabaseHooksRunner(collection, TestLogger<DatabaseHooksRunner>.Instance);
    }
}
