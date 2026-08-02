using System.Text;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Database.Ddl;

namespace Grillisoft.Tools.DatabaseDeploy.Database.Tests.Ddl;

/// <summary>
/// The orchestration every provider inherits: what reaches the script, what happens to an object
/// that cannot be scripted, and what the run reports about itself.
/// </summary>
public class SchemaDdlGeneratorTests
{
    private static readonly DbObject Customer = new("CUSTOMER", "TABLE");
    private static readonly DbObject Orders = new("ORDERS", "TABLE");
    private static readonly DbObject CustomerView = new("V_CUSTOMER", "VIEW");

    private static int RankOf(string type) => type switch
    {
        "TABLE" => 10,
        "VIEW" => 20,
        _ => int.MaxValue,
    };

    /// <summary>
    /// A generator over canned results: whatever <see cref="Objects"/> and
    /// <see cref="Dependencies"/> say is discovered, and one statement per object unless
    /// <see cref="Failing"/> names it.
    /// </summary>
    private sealed class FakeGenerator(RecordingLogger logger) : SchemaDdlGenerator("HR", "database", logger)
    {
        public List<DbObject> Objects { get; } = [];

        public List<(DbObject DbObject, DbObject DependsOn)> Dependencies { get; } = [];

        public HashSet<string> Failing { get; } = [];

        public List<string> Epilogue { get; } = [];

        public bool Prepared { get; private set; }

        public List<DbObject> Scripted { get; } = [];

        protected override Func<string, int> RankOf => SchemaDdlGeneratorTests.RankOf;

        protected override DdlScriptWriter CreateWriter(StreamWriter stream) => new(stream, "-- ", "GO");

        protected override Task Prepare(CancellationToken cancellationToken)
        {
            Prepared = true;
            return Task.CompletedTask;
        }

        protected override Task<(List<DbObject> Objects, List<(DbObject DbObject, DbObject DependsOn)> Dependencies)>
            Discover(CancellationToken cancellationToken)
            => Task.FromResult((Objects, Dependencies));

        protected override Task<IReadOnlyList<string>> Script(DbObject dbObject, CancellationToken cancellationToken)
        {
            if (Failing.Contains(dbObject.Name))
                throw new InvalidOperationException($"cannot script {dbObject.Name}");

            Scripted.Add(dbObject);
            return Task.FromResult<IReadOnlyList<string>>([$"CREATE {dbObject.Type} {dbObject.Name}"]);
        }

        protected async override Task WriteEpilogue(
            DdlScriptWriter writer,
            IReadOnlyList<DbObject> ordered,
            CancellationToken cancellationToken)
        {
            foreach (var statement in Epilogue)
                await writer.WriteStatement(statement);

            CountStatements(Epilogue.Count);
        }
    }

    private static async Task<string> Generate(FakeGenerator generator)
    {
        var (script, exception) = await TryGenerate(generator);
        return exception is null ? script : throw exception;
    }

    /// <summary>
    /// The script and whatever <see cref="SchemaDdlGenerator.Generate"/> threw. Both matter
    /// together: the file is written out in full before a failed run reports itself, and that is
    /// exactly what makes it inspectable.
    /// </summary>
    private static async Task<(string Script, Exception? Exception)> TryGenerate(FakeGenerator generator)
    {
        using var stream = new MemoryStream();
        Exception? thrown = null;

        await using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
        {
            try
            {
                await generator.Generate(writer, CancellationToken.None);
            }
            catch (Exception ex)
            {
                thrown = ex;
            }
        }

        return (Encoding.UTF8.GetString(stream.ToArray()), thrown);
    }

    [Test]
    public async Task Generate_ShouldWriteAHeaderAndEveryObjectInDependencyOrder()
    {
        // Arrange
        var logger = new RecordingLogger();
        var generator = new FakeGenerator(logger);
        generator.Objects.AddRange([CustomerView, Customer]);
        generator.Dependencies.Add((CustomerView, Customer));

        // Act
        var script = await Generate(generator);

        // Assert
        generator.Prepared.Should().BeTrue("the session is set up before anything is read");
        script.Should().Contain("-- Database HR - 2 object(s)")
              .And.Contain("-- Do not edit: regenerate instead");

        script.IndexOf("CREATE TABLE CUSTOMER", StringComparison.Ordinal)
              .Should().BeLessThan(script.IndexOf("CREATE VIEW V_CUSTOMER", StringComparison.Ordinal));
    }

    /// <summary>
    /// Nothing to script is not a failure: the file is still written, so the step it belongs to
    /// exists and can be deployed.
    /// </summary>
    [Test]
    public async Task Generate_WhenThereIsNothingToScript_ShouldWriteOnlyAHeader()
    {
        // Arrange
        var logger = new RecordingLogger();
        var generator = new FakeGenerator(logger);

        // Act
        var script = await Generate(generator);

        // Assert
        script.Should().Contain("-- Database HR - 0 object(s)").And.NotContain("GO");
        logger.Warnings.Should().Contain(message => message.Contains("No scriptable object found"));
    }

    /// <summary>
    /// The whole point of the per-object try: one object the server refuses must not cost the other
    /// hundreds, and what went wrong has to be readable in the script itself.
    /// </summary>
    [Test]
    public async Task Generate_WhenAnObjectCannotBeScripted_ShouldKeepGoingAndThrowAtTheEnd()
    {
        // Arrange
        var logger = new RecordingLogger();
        var generator = new FakeGenerator(logger);
        generator.Objects.AddRange([Customer, Orders, CustomerView]);
        generator.Failing.Add("ORDERS");

        // Act
        var (script, exception) = await TryGenerate(generator);

        // Assert
        exception.Should().BeOfType<DdlGenerationException>()
                 .Which.Failures.Should().ContainSingle()
                 .Which.Object.Should().Be(Orders.Key);
        exception!.Message.Should().Contain("object(s) of database HR");

        generator.Scripted.Should().Equal(Customer, CustomerView);

        script.Should().Contain("CREATE TABLE CUSTOMER")
              .And.Contain("CREATE VIEW V_CUSTOMER")
              .And.Contain("-- !! FAILED to script TABLE ORDERS: cannot script ORDERS")
              .And.Contain("-- !! ORDERS---TABLE could not be scripted: cannot script ORDERS");
    }

    /// <summary>
    /// A cycle is broken rather than fatal, but the objects in it may be created invalid, so the
    /// script has to say which ones.
    /// </summary>
    [Test]
    public async Task Generate_WhenADependencyCycleIsBroken_ShouldReportItInTheFooter()
    {
        // Arrange
        var logger = new RecordingLogger();
        var generator = new FakeGenerator(logger);
        generator.Objects.AddRange([Customer, Orders]);
        generator.Dependencies.AddRange([(Customer, Orders), (Orders, Customer)]);

        // Act
        var script = await Generate(generator);

        // Assert
        script.Should().Contain("-- Dependency cycle, objects may be created invalid:")
              .And.Contain("CUSTOMER---TABLE")
              .And.Contain("ORDERS---TABLE");
    }

    /// <summary>
    /// A clean run writes no footer at all, so the last thing in the file is a statement rather
    /// than a block of comments no parser has to make sense of.
    /// </summary>
    [Test]
    public async Task Generate_WhenNothingWentWrong_ShouldNotWriteAFooter()
    {
        // Arrange
        var generator = new FakeGenerator(new RecordingLogger());
        generator.Objects.Add(Customer);

        // Act
        var script = await Generate(generator);

        // Assert
        script.Should().NotContain("!!").And.NotContain("Dependency cycle");
    }

    [Test]
    public async Task Generate_ShouldWriteTheEpilogueAfterTheObjects()
    {
        // Arrange
        var logger = new RecordingLogger();
        var generator = new FakeGenerator(logger);
        generator.Objects.Add(Customer);
        generator.Epilogue.Add("COMMENT ON TABLE CUSTOMER IS 'hello'");

        // Act
        var script = await Generate(generator);

        // Assert
        script.IndexOf("CREATE TABLE CUSTOMER", StringComparison.Ordinal)
              .Should().BeLessThan(script.IndexOf("COMMENT ON TABLE", StringComparison.Ordinal));

        // one statement for the object, one for the comment
        logger.Messages.Should().Contain(message => message.Contains("into 2 statements"));
    }

    [Test]
    public async Task Generate_ShouldSummariseWhatItScriptedByType()
    {
        // Arrange
        var logger = new RecordingLogger();
        var generator = new FakeGenerator(logger);
        generator.Objects.AddRange([Customer, Orders, CustomerView]);

        // Act
        await Generate(generator);

        // Assert
        logger.Messages.Should().Contain(message =>
            message.Contains("Scripted 3/3 objects") && message.Contains("TABLE (2), VIEW (1)"));
    }
}
