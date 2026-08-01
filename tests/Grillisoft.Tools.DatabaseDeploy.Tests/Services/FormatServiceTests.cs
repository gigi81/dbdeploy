using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Exceptions;
using Grillisoft.Tools.DatabaseDeploy.Options;
using Grillisoft.Tools.DatabaseDeploy.Services;
using Grillisoft.Tools.DatabaseDeploy.Tests.Mocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    /// <summary>The step is still in a release branch file, so it has not been released yet.</summary>
    private static MockFileSystem CreateFileSystem() =>
        new(new Dictionary<string, MockFileData>
        {
            ["/path/main.csv"] = new("MyDb,_Init"),
            ["/path/release_1.1.csv"] = new("MyDb,TKT001.Change"),
            [InitPath] = new("select 1"),
            [DeployPath] = new("select 2"),
            [RollbackPath] = new("select 3")
        });

    /// <summary>
    /// The same layout after the release: the step has been moved into the default branch file, so
    /// its deploy script is out there and its migration hash has to stay put.
    /// </summary>
    private static MockFileSystem CreateReleasedFileSystem() =>
        new(new Dictionary<string, MockFileData>
        {
            ["/path/main.csv"] = new("MyDb,_Init\nMyDb,TKT001.Change"),
            [InitPath] = new("select 1"),
            [DeployPath] = new("select 2"),
            [RollbackPath] = new("select 3")
        });

    [Test]
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
    [Test]
    public async Task Execute_ShouldLeaveInitScriptsAlone()
    {
        var fileSystem = CreateFileSystem();

        await CreateService(fileSystem, out _).Execute(CancellationToken.None);

        fileSystem.File.ReadAllText(InitPath).Should().Be("select 1");
    }

    [Test]
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
    [Test]
    public async Task Execute_WhenVerificationFails_ShouldLeaveTheFileAloneAndReportAFailure()
    {
        var fileSystem = CreateFileSystem();
        var formatter = new SqlFormatterMock(verificationError: "token 3 was lost");

        var result = await CreateService(fileSystem, out _, formatter).Execute(CancellationToken.None);

        result.Should().Be(2, "both the deploy and the rollback script failed");
        fileSystem.File.ReadAllText(DeployPath).Should().Be("select 2");
    }

    [Test]
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
    /// Formatting is a file operation: the dialect comes out of the configuration, so nothing ever
    /// builds a database, let alone opens a connection.
    /// </summary>
    [Test]
    public async Task Execute_ShouldNeverBuildADatabase()
    {
        var fileSystem = CreateFileSystem();
        var formatter = new SqlFormatterMock();

        var service = CreateService(
            fileSystem,
            out _,
            databases: new OfflineDatabasesCollectionMock("MyDb", formatter));

        var result = await service.Execute(CancellationToken.None);

        result.Should().Be(0);
        formatter.Formatted.Should().HaveCount(2, "the deploy and the rollback script");
        fileSystem.File.ReadAllText(DeployPath).Should().Be("SELECT 2");
    }

    /// <summary>
    /// A step in the default branch file has been released, so its deploy script is out there and
    /// its MD5 is the migration hash the databases that ran it recorded. Rewriting it would break
    /// that, so it is left alone - and said so, since the file looking fine is not the point.
    /// </summary>
    [Test]
    public async Task Execute_WhenTheStepIsReleased_ShouldNotFormatTheDeployScript()
    {
        var fileSystem = CreateReleasedFileSystem();
        var logger = new RecordingLogger<FormatService>();

        var result = await CreateService(fileSystem, out _, logger: logger).Execute(CancellationToken.None);

        result.Should().Be(0);
        fileSystem.File.ReadAllText(DeployPath).Should().Be("select 2", "it is released");
        fileSystem.File.ReadAllText(RollbackPath).Should().Be("SELECT 3", "only the deploy script is hashed");

        logger.Warnings.Should().ContainSingle()
            .Which.Should().Contain("TKT001.Change").And.Contain("main").And.Contain("--force");
    }

    /// <summary>
    /// The warning is about the file having been left alone, so it does not depend on whether
    /// formatting would have changed anything.
    /// </summary>
    [Test]
    public async Task Execute_WhenAReleasedScriptNeedsNoChange_ShouldStillWarnItWasSkipped()
    {
        var fileSystem = CreateReleasedFileSystem();
        fileSystem.File.WriteAllText(DeployPath, "SELECT 2");
        var logger = new RecordingLogger<FormatService>();

        await CreateService(fileSystem, out _, logger: logger).Execute(CancellationToken.None);

        logger.Warnings.Should().ContainSingle().Which.Should().Contain("TKT001.Change");
    }

    /// <summary>--force is the way to reformat released scripts anyway, hash mismatch and all.</summary>
    [Test]
    public async Task Execute_WhenForced_ShouldFormatTheReleasedDeployScriptAndWarn()
    {
        var fileSystem = CreateReleasedFileSystem();
        var logger = new RecordingLogger<FormatService>();

        var options = new FormatOptions { Path = "/path", Force = true };
        var service = CreateService(fileSystem, out _, options: options, logger: logger);

        await service.Execute(CancellationToken.None);

        fileSystem.File.ReadAllText(DeployPath).Should().Be("SELECT 2");
        logger.Warnings.Should().ContainSingle().Which.Should().Contain("changed its migration hash");
    }

    /// <summary>
    /// A step that is still in a release branch file has not been released yet, so formatting it is
    /// exactly what you are supposed to do.
    /// </summary>
    [Test]
    public async Task Execute_WhenTheStepIsNotReleasedYet_ShouldFormatWithoutWarning()
    {
        var fileSystem = CreateFileSystem();
        var logger = new RecordingLogger<FormatService>();

        await CreateService(fileSystem, out _, logger: logger).Execute(CancellationToken.None);

        logger.Warnings.Should().BeEmpty();
        fileSystem.File.ReadAllText(DeployPath).Should().Be("SELECT 2");
    }

    /// <summary>
    /// Which dialect a script was laid out with is worked out from the folder layout, so the run
    /// has to say which one it used.
    /// </summary>
    [Test]
    public async Task Execute_ShouldLogTheDialectItFormattedWith()
    {
        var fileSystem = CreateFileSystem();
        var logger = new RecordingLogger<FormatService>();

        await CreateService(fileSystem, out _, logger: logger).Execute(CancellationToken.None);

        // The service logs the path the file system hands it, which on windows is C:\path\MyDb\...
        // rather than the /path/MyDb/... this fixture is written with.
        var deployPath = fileSystem.FileInfo.New(DeployPath).FullName;

        logger.Entries.Select(entry => entry.Message)
            .Should().Contain(message => message.Contains(deployPath) && message.Contains("mock"));
    }

    /// <summary>
    /// A database that is configured but has no provider - or is not configured at all - still
    /// formats, falling back to the provider named on the command line.
    /// </summary>
    [Test]
    public async Task Execute_WhenTheDatabaseHasNoConfiguredDialect_ShouldUseTheNamedProvider()
    {
        var fileSystem = CreateFileSystem();
        var fallback = new SqlFormatterMock();

        var service = CreateService(
            fileSystem,
            out _,
            options: new FormatOptions { Path = "/path", Provider = "mock" },
            factory: new DatabaseFactoryMock { SqlFormatter = fallback },
            databases: new OfflineDatabasesCollectionMock("MyDb", formatter: null));

        var result = await service.Execute(CancellationToken.None);

        result.Should().Be(0);
        fallback.Formatted.Should().HaveCount(2);
        fileSystem.File.ReadAllText(DeployPath).Should().Be("SELECT 2");
    }

    // ------------------------------------------------------- directory mode

    /// <summary>
    /// Globs pick the files, so nothing is filtered out - including the init scripts that branch
    /// mode deliberately leaves alone.
    /// </summary>
    [Test]
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

    [Test]
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

    [Test]
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
    [Test]
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
    [Test]
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

    [Test]
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

    /// <summary>Directory mode must never reach for a database either.</summary>
    [Test]
    public async Task Execute_WhenGlobsAreGiven_ShouldNeverBuildADatabase()
    {
        var fileSystem = CreateFileSystem();
        var formatter = new SqlFormatterMock();

        var options = new FormatOptions { Path = "/path", Include = ["**/*.sql"] };
        var service = CreateService(
            fileSystem,
            out _,
            options: options,
            databases: new OfflineDatabasesCollectionMock("MyDb", formatter));

        var result = await service.Execute(CancellationToken.None);

        result.Should().Be(0);
        formatter.Formatted.Should().HaveCount(3, "every script sits under the MyDb folder");
    }

    /// <summary>Globs are relative to the path, and a branch layout is not needed at all.</summary>
    [Test]
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

    private static FormatService CreateService(
        IFileSystem fileSystem,
        out DatabaseMock database,
        ISqlFormatter? formatter = null,
        GlobalSettings? settings = null,
        FormatOptions? options = null,
        IDatabaseFactory? factory = null,
        IDatabasesCollection? databases = null,
        RecordingLogger<FormatService>? logger = null)
    {
        database = new DatabaseMock("MyDb", new ScriptParserMock(), formatter ?? new SqlFormatterMock());

        var services = new TestServiceCollection<FormatService>()
            .AddSingleton(options ?? new FormatOptions { Path = "/path" })
            .AddSingleton(fileSystem)
            .AddSingleton(factory ?? new DatabaseFactoryMock())
            .AddSingleton(databases ?? new DatabasesCollectionMock(database))
            .AddSingleton(ExtensionsOptions.Create(settings ?? new GlobalSettings { InitStepName = "_Init" }));

        if (logger is not null)
        {
            services.AddSingleton<ILogger<FormatService>>(logger);
            //the warnings about a step are written through the logger of its database
            services.AddSingleton<IDatabaseLoggerFactory>(new DatabaseLoggerFactory(new RecordingLoggerFactory(logger)));
        }

        return services.BuildServiceProvider().GetRequiredService<FormatService>();
    }

    /// <summary>
    /// Knows the dialect of its databases but refuses to build one, which is what formatting has to
    /// get by on.
    /// </summary>
    private sealed class OfflineDatabasesCollectionMock : IDatabasesCollection
    {
        private readonly string _name;
        private readonly ISqlFormatter? _formatter;

        public OfflineDatabasesCollectionMock(string name, ISqlFormatter? formatter)
        {
            _name = name;
            _formatter = formatter;
        }

        public IReadOnlyCollection<string> Databases => [_name];

        public Task<IDatabase> GetDatabase(string name, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("no route to host");

        public ISqlFormatter? GetSqlFormatter(string name) =>
            name.Equals(_name, StringComparison.InvariantCultureIgnoreCase) ? _formatter : null;

        public DatabaseHooks GetHooks(string name) => DatabaseHooks.None;
    }
}
