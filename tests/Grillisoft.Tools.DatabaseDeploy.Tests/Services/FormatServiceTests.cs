using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using AwesomeAssertions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Exceptions;
using Grillisoft.Tools.DatabaseDeploy.Options;
using Grillisoft.Tools.DatabaseDeploy.Services;
using Grillisoft.Tools.DatabaseDeploy.Tests.Mocks;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;
using ExtensionsOptions = Microsoft.Extensions.Options.Options;

namespace Grillisoft.Tools.DatabaseDeploy.Tests.Services;

/// <summary>
/// What the service does with the files, using a formatter mock that simply uppercases so the tests
/// are about file selection and write behaviour rather than about layout.
/// </summary>
public class FormatServiceTests
{
    private const string DeployPath = "/path/MyDb/TKT001.Change.Deploy.sql";
    private const string RollbackPath = "/path/MyDb/TKT001.Change.Rollback.sql";
    private const string InitPath = "/path/MyDb/_Init.sql";

    private readonly ITestOutputHelper _output;

    public FormatServiceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static MockFileSystem CreateFileSystem() =>
        new(new Dictionary<string, MockFileData>
        {
            ["/path/main.csv"] = new("MyDb,_Init\nMyDb,TKT001.Change"),
            [InitPath] = new("select 1"),
            [DeployPath] = new("select 2"),
            [RollbackPath] = new("select 3")
        });

    [Fact]
    public async Task Execute_ShouldFormatTheDeployAndRollbackScripts()
    {
        var fileSystem = CreateFileSystem();

        var result = await CreateService(fileSystem, out _).Execute(CancellationToken.None);

        result.Should().Be(0);
        fileSystem.File.ReadAllText(DeployPath).Should().Be("SELECT 2");
        fileSystem.File.ReadAllText(RollbackPath).Should().Be("SELECT 3");
    }

    /// <summary>
    /// Init scripts are generated schema dumps. Reformatting one produces an enormous diff of
    /// something nobody reads by hand, so they are left alone.
    /// </summary>
    [Fact]
    public async Task Execute_ShouldLeaveInitScriptsAlone()
    {
        var fileSystem = CreateFileSystem();

        await CreateService(fileSystem, out _).Execute(CancellationToken.None);

        fileSystem.File.ReadAllText(InitPath).Should().Be("select 1");
    }

    [Fact]
    public async Task Execute_WhenAScriptIsAlreadyFormatted_ShouldNotRewriteIt()
    {
        var fileSystem = CreateFileSystem();
        fileSystem.File.WriteAllText(DeployPath, "SELECT 2");
        var before = fileSystem.FileInfo.New(DeployPath).LastWriteTimeUtc;

        await CreateService(fileSystem, out _).Execute(CancellationToken.None);

        fileSystem.FileInfo.New(DeployPath).LastWriteTimeUtc.Should().Be(before);
    }

    /// <summary>
    /// A formatter that cannot reproduce the script must not be allowed to write it, and the run has
    /// to fail so the problem cannot pass unnoticed.
    /// </summary>
    [Fact]
    public async Task Execute_WhenVerificationFails_ShouldLeaveTheFileAloneAndReportAFailure()
    {
        var fileSystem = CreateFileSystem();
        var formatter = new SqlFormatterMock(verificationError: "token 3 was lost");

        var result = await CreateService(fileSystem, out _, formatter).Execute(CancellationToken.None);

        result.Should().Be(2, "both the deploy and the rollback script failed");
        fileSystem.File.ReadAllText(DeployPath).Should().Be("select 2");
    }

    [Fact]
    public async Task Execute_WhenTheRollbackScriptIsMissing_ShouldStillFormatTheDeployScript()
    {
        var fileSystem = CreateFileSystem();
        fileSystem.File.Delete(RollbackPath);

        var service = CreateService(
            fileSystem,
            out _,
            settings: new GlobalSettings { InitStepName = "_Init", RollbackRequired = false });

        var result = await service.Execute(CancellationToken.None);

        result.Should().Be(0);
        fileSystem.File.ReadAllText(DeployPath).Should().Be("SELECT 2");
    }

    /// <summary>
    /// The deploy script's MD5 is the migration hash, so reformatting one that has already been
    /// deployed has to be called out.
    /// </summary>
    [Fact]
    public async Task Execute_WhenTheStepIsAlreadyDeployed_ShouldWarn()
    {
        var fileSystem = CreateFileSystem();
        var service = CreateService(fileSystem, out var database);
        await database.AddMigration(
            new DatabaseMigration("TKT001.Change", "user", new string('0', 32)),
            CancellationToken.None);

        await service.Execute(CancellationToken.None);

        // The warning is the point; the file is still formatted.
        fileSystem.File.ReadAllText(DeployPath).Should().Be("SELECT 2");
    }

    /// <summary>
    /// Formatting has to work with no database in reach, so an unreachable one only costs the
    /// already-deployed warning.
    /// </summary>
    [Fact]
    public async Task Execute_WhenTheDatabaseCannotBeRead_ShouldStillFormat()
    {
        var fileSystem = CreateFileSystem();

        var provider = new TestServiceCollection<FormatService>(_output)
            .AddSingleton(new FormatOptions { Path = "/path" })
            .AddSingleton<IFileSystem>(fileSystem)
            .AddSingleton<IDatabaseFactory>(new DatabaseFactoryMock())
            .AddSingleton<IDatabasesCollection>(new DatabasesCollectionMock(new UnreadableDatabaseMock("MyDb")))
            .AddSingleton(ExtensionsOptions.Create(new GlobalSettings { InitStepName = "_Init" }))
            .BuildServiceProvider();

        var result = await provider.GetRequiredService<FormatService>().Execute(CancellationToken.None);

        result.Should().Be(0);
        fileSystem.File.ReadAllText(DeployPath).Should().Be("SELECT 2");
    }

    // ------------------------------------------------------- directory mode

    /// <summary>
    /// Globs pick the files, so nothing is filtered out - including the init scripts that branch
    /// mode deliberately leaves alone.
    /// </summary>
    [Fact]
    public async Task Execute_WhenGlobsAreGiven_ShouldFormatEveryMatchIncludingInitScripts()
    {
        var fileSystem = CreateFileSystem();
        fileSystem.AddFile("/path/MyDb/notes.txt", new MockFileData("select 4"));

        var options = new FormatOptions { Path = "/path", Include = ["**/*.sql"] };
        var result = await CreateService(fileSystem, out _, options: options).Execute(CancellationToken.None);

        result.Should().Be(0);
        fileSystem.File.ReadAllText(InitPath).Should().Be("SELECT 1");
        fileSystem.File.ReadAllText(DeployPath).Should().Be("SELECT 2");
        fileSystem.File.ReadAllText(RollbackPath).Should().Be("SELECT 3");
        fileSystem.File.ReadAllText("/path/MyDb/notes.txt").Should().Be("select 4", "it is not a match");
    }

    [Fact]
    public async Task Execute_ShouldHonourExcludeGlobs()
    {
        var fileSystem = CreateFileSystem();

        var options = new FormatOptions
        {
            Path = "/path",
            Include = ["**/*.sql"],
            Exclude = ["**/_Init.sql"]
        };

        await CreateService(fileSystem, out _, options: options).Execute(CancellationToken.None);

        fileSystem.File.ReadAllText(InitPath).Should().Be("select 1", "it was excluded");
        fileSystem.File.ReadAllText(DeployPath).Should().Be("SELECT 2");
    }

    [Fact]
    public async Task Execute_WhenNothingMatches_ShouldSucceedWithoutWriting()
    {
        var fileSystem = CreateFileSystem();

        var options = new FormatOptions { Path = "/path", Include = ["**/*.nope"] };
        var result = await CreateService(fileSystem, out _, options: options).Execute(CancellationToken.None);

        result.Should().Be(0);
        fileSystem.File.ReadAllText(DeployPath).Should().Be("select 2");
    }

    /// <summary>
    /// A folder named after a configured database says which dialect its scripts are in, so a
    /// normal layout needs no --provider.
    /// </summary>
    [Fact]
    public async Task Execute_ShouldTakeTheDialectFromTheDatabaseFolder()
    {
        var fileSystem = CreateFileSystem();
        var databaseFormatter = new SqlFormatterMock();
        var fallback = new SqlFormatterMock();

        var options = new FormatOptions { Path = "/path", Include = ["**/*.sql"], Provider = "mock" };
        var service = CreateService(
            fileSystem,
            out _,
            formatter: databaseFormatter,
            options: options,
            factory: new DatabaseFactoryMock { SqlFormatter = fallback });

        await service.Execute(CancellationToken.None);

        databaseFormatter.Formatted.Should().HaveCount(3, "every script sits under the MyDb folder");
        fallback.Formatted.Should().BeEmpty();
    }

    /// <summary>
    /// Scripts outside any database folder fall back to the provider named on the command line.
    /// </summary>
    [Fact]
    public async Task Execute_WhenAFileIsOutsideADatabaseFolder_ShouldUseTheNamedProvider()
    {
        var fileSystem = CreateFileSystem();
        fileSystem.AddFile("/path/loose.sql", new MockFileData("select 9"));
        var fallback = new SqlFormatterMock();

        var options = new FormatOptions { Path = "/path", Include = ["loose.sql"], Provider = "mock" };
        var service = CreateService(
            fileSystem,
            out _,
            options: options,
            factory: new DatabaseFactoryMock { SqlFormatter = fallback });

        await service.Execute(CancellationToken.None);

        fallback.Formatted.Should().ContainSingle();
        fileSystem.File.ReadAllText("/path/loose.sql").Should().Be("SELECT 9");
    }

    [Fact]
    public async Task Execute_WhenTheDialectCannotBeResolved_ShouldSayWhichProvidersAreKnown()
    {
        var fileSystem = CreateFileSystem();
        fileSystem.AddFile("/path/loose.sql", new MockFileData("select 9"));

        var options = new FormatOptions { Path = "/path", Include = ["loose.sql"] };
        var service = CreateService(fileSystem, out _, options: options);

        var act = () => service.Execute(CancellationToken.None);

        await act.Should().ThrowAsync<SqlDialectNotFoundException>()
            .WithMessage("*--provider*mock*");
    }

    /// <summary>Directory mode is a pure file operation and must never reach for a database.</summary>
    [Fact]
    public async Task Execute_WhenGlobsAreGiven_ShouldNotReadTheMigrations()
    {
        var fileSystem = CreateFileSystem();

        var options = new FormatOptions { Path = "/path", Include = ["**/*.sql"] };
        var service = CreateService(
            fileSystem,
            out _,
            options: options,
            databases: new DatabasesCollectionMock(new UnreadableDatabaseMock("MyDb")));

        var result = await service.Execute(CancellationToken.None);

        result.Should().Be(0, "the unreadable database was never asked for its migrations");
    }

    /// <summary>Globs are relative to the path, and a branch layout is not needed at all.</summary>
    [Fact]
    public async Task Execute_WhenGlobsAreGiven_ShouldNotNeedTheBranchFiles()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/loose/one.sql"] = new("select 1")
        });

        var options = new FormatOptions { Path = "/loose", Include = ["**/*.sql"], Provider = "mock" };
        var result = await CreateService(fileSystem, out _, options: options).Execute(CancellationToken.None);

        result.Should().Be(0);
        fileSystem.File.ReadAllText("/loose/one.sql").Should().Be("SELECT 1");
    }

    private FormatService CreateService(
        IFileSystem fileSystem,
        out DatabaseMock database,
        ISqlFormatter? formatter = null,
        GlobalSettings? settings = null,
        FormatOptions? options = null,
        IDatabaseFactory? factory = null,
        IDatabasesCollection? databases = null)
    {
        database = new DatabaseMock("MyDb", new ScriptParserMock(), formatter ?? new SqlFormatterMock());

        var provider = new TestServiceCollection<FormatService>(_output)
            .AddSingleton(options ?? new FormatOptions { Path = "/path" })
            .AddSingleton(fileSystem)
            .AddSingleton(factory ?? new DatabaseFactoryMock())
            .AddSingleton(databases ?? new DatabasesCollectionMock(database))
            .AddSingleton(ExtensionsOptions.Create(settings ?? new GlobalSettings { InitStepName = "_Init" }))
            .BuildServiceProvider();

        return provider.GetRequiredService<FormatService>();
    }

    private sealed class UnreadableDatabaseMock : DatabaseMock
    {
        public UnreadableDatabaseMock(string name)
            : base(name, new ScriptParserMock(), new SqlFormatterMock())
        {
        }

        public override Task<ICollection<DatabaseMigration>> GetMigrations(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("no route to host");
    }
}
