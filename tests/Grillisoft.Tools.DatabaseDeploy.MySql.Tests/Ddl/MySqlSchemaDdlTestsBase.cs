using System.IO.Abstractions.TestingHelpers;
using System.Text;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Tests;
using Grillisoft.Tools.DatabaseDeploy.Tests.Databases;
using MySqlConnector;

namespace Grillisoft.Tools.DatabaseDeploy.MySql.Tests.Ddl;

/// <summary>
/// End to end check of the schema DDL generation: build a schema that uses the features a real
/// application database uses, script it, wipe the database and replay the script through the very
/// parser dbdeploy uses at deploy time. Whatever comes back has to match what was there before.
/// </summary>
/// <remarks>
/// The same body runs against MySQL and MariaDB - see <see cref="MySqlSchemaDdlTests"/> and
/// <see cref="MariaDbSchemaDdlTests"/> - because the provider makes no distinction between the two
/// and the places where they differ are exactly the places this is guarding.
/// </remarks>
[InheritsTests]
public abstract class MySqlSchemaDdlTestsBase : DatabaseTest<MySqlDatabase>
{
    protected MySqlSchemaDdlTestsBase(IDatabaseFixture fixture)
        : base(fixture)
    {
    }

    protected override IDatabaseFactory CreateDatabaseFactory()
    {
        return new MySqlDatabaseFactory(
            new MySqlScriptParser(),
            this.GlobalSettingsOptions,
            this.LoggerFactory);
    }

    protected override string ProviderName => MySqlDatabaseFactory.ProviderName;

    /// <summary>
    /// Everything here exists because it breaks a generator that does the obvious thing: an auto
    /// increment counter that has moved off 1, a stored generated column, two tables referencing
    /// each other (which no ordering of inline foreign keys can survive), a routine whose body is
    /// full of semicolons, a view on a view, a view calling a function, and literals holding a
    /// quote and a semicolon.
    /// </summary>
    private static readonly string[] CommonSchema =
    [
        """
        CREATE TABLE customer (
          id INT NOT NULL AUTO_INCREMENT,
          name VARCHAR(100) NOT NULL,
          email VARCHAR(200),
          last_order_id INT NULL,
          note VARCHAR(400) DEFAULT 'careful: it''s got a ; in it',
          created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
          PRIMARY KEY (id),
          UNIQUE KEY uq_customer_email (email)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
        """,
        // the counter has to move past 1, or stripping it would be unobservable
        "INSERT INTO customer (name, email) VALUES ('a', 'a@example.com'), ('b', 'b@example.com')",
        """
        CREATE TABLE orders (
          id INT NOT NULL AUTO_INCREMENT,
          customer_id INT NOT NULL,
          total DECIMAL(10,2) NOT NULL DEFAULT 0.00,
          total_with_tax DECIMAL(12,2) GENERATED ALWAYS AS (total * 1.22) STORED,
          PRIMARY KEY (id),
          KEY ix_orders_customer (customer_id),
          CONSTRAINT fk_orders_customer FOREIGN KEY (customer_id) REFERENCES customer (id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
        """,
        // the mutual reference: with inline keys there is no order in which these two can be created
        "ALTER TABLE customer ADD CONSTRAINT fk_customer_last_order FOREIGN KEY (last_order_id) REFERENCES orders (id)",
        """
        CREATE FUNCTION fn_tax(amount DECIMAL(10,2)) RETURNS DECIMAL(10,2)
        DETERMINISTIC
        BEGIN
          -- don't remove this comment: its apostrophe has broken a parser before
          DECLARE rate DECIMAL(4,2);
          SET rate = 0.22;
          RETURN amount * rate;
        END
        """,
        """
        CREATE VIEW v_order_totals AS
        SELECT o.id AS order_id, o.customer_id, o.total, fn_tax(o.total) AS tax
        FROM orders o
        """,
        // a view on a view: it can only be created after the one it selects from
        """
        CREATE VIEW v_big_orders AS
        SELECT order_id, total FROM v_order_totals WHERE total > 100
        """,
        """
        CREATE PROCEDURE pr_add_order(IN p_customer INT, IN p_total DECIMAL(10,2))
        BEGIN
          INSERT INTO orders (customer_id, total) VALUES (p_customer, p_total);
        END
        """,
        """
        CREATE TRIGGER trg_orders_default_total
        BEFORE INSERT ON orders
        FOR EACH ROW
        BEGIN
          IF NEW.total IS NULL THEN
            SET NEW.total = 0;
          END IF;
        END
        """,
    ];

    /// <summary>
    /// Statements only one of the two servers understands. MariaDB adds a sequence, which MySQL has
    /// no equivalent of.
    /// </summary>
    protected virtual IEnumerable<string> EngineSpecificSchema => [];

    [Test]
    [Category(TestCategories.Docker)]
    public async Task GenerateSchemaDdl_ShouldProduceAScriptThatRebuildsTheSchema(CancellationToken cancellationToken)
    {
        // arrange
        var sut = await this.CreateDatabase(cancellationToken);
        await DropEverything();

        foreach (var statement in CommonSchema.Concat(EngineSpecificSchema))
            await sut.RunScript(statement, CancellationToken.None);

        var expectedTables = await GetTables();
        var expectedColumns = await GetColumns();
        var expectedIndexes = await GetIndexes();
        var expectedForeignKeys = await GetForeignKeys();
        var expectedRoutines = await GetRoutines();
        var expectedTriggers = await GetTriggers();

        expectedTables.Should().NotBeEmpty();
        expectedForeignKeys.Should().NotBeEmpty();

        // act
        var script = await GenerateScript(sut);
        TestContext.Current?.OutputWriter.WriteLine(script);

        await DropEverything();
        (await GetTables()).Should().BeEmpty("the database must be wiped before replaying the script");

        await Replay(sut, script);

        // assert
        (await GetTables()).Should().BeEquivalentTo(expectedTables);
        (await GetColumns()).Should().BeEquivalentTo(expectedColumns);
        (await GetIndexes()).Should().BeEquivalentTo(expectedIndexes);
        (await GetForeignKeys()).Should().BeEquivalentTo(expectedForeignKeys);
        (await GetRoutines()).Should().BeEquivalentTo(expectedRoutines);
        (await GetTriggers()).Should().BeEquivalentTo(expectedTriggers);

        // the counter a table has reached is data: leaving it in makes the file differ on every run
        script.Should().NotContain("AUTO_INCREMENT=");

        // the user who happened to define an object cannot be assumed to exist where it is replayed
        script.Should().NotContain("DEFINER=");

        // a view definition carrying the name of the database it was read from replays nowhere else
        script.Should().NotContain($"`{await GetDatabaseName()}`.");

        // dbdeploy owns the migrations table; a script that recreates it fights the tool
        script.Should().NotContain("__Migrations");

        // a routine body holds its own semicolons and has to be wrapped for the parser
        script.Should().Contain("DELIMITER");
    }

    private static async Task<string> GenerateScript(MySqlDatabase database)
    {
        using var stream = new MemoryStream();
        await using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
        {
            await database.GenerateSchemaDdl(writer, CancellationToken.None);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Replays the script the way the deploy command does, statement by statement, through the
    /// MySQL script parser.
    /// </summary>
    private static async Task Replay(MySqlDatabase database, string script)
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile("/scripts/init.sql", new MockFileData(script));

        var parser = new MySqlScriptParser();
        var executed = 0;

        await foreach (var statement in parser.Parse(fileSystem.FileInfo.New("/scripts/init.sql"), CancellationToken.None))
        {
            try
            {
                await database.RunScript(statement, CancellationToken.None);
                executed++;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Statement {executed + 1} of the generated script failed: {ex.Message}{Environment.NewLine}{statement}", ex);
            }
        }

        TestContext.Current?.OutputWriter.WriteLine($"Replayed {executed} statements");
        executed.Should().BeGreaterThan(0);
    }

    private async Task<List<string>> Read(string sql)
    {
        await using var connection = new MySqlConnection(this.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(reader.IsDBNull(0) ? string.Empty : reader.GetString(0));

        return result;
    }

    private async Task<string> GetDatabaseName()
        => (await Read("SELECT DATABASE()")).Single();

    private Task<List<string>> GetTables() => Read("""
        SELECT CONCAT(TABLE_TYPE, ' ', TABLE_NAME)
        FROM information_schema.TABLES
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME <> '__Migrations'
        ORDER BY 1
        """);

    /// <summary>
    /// The column definition down to the parts a generated script has to reproduce exactly: the
    /// type, nullability, the default, and whether the value is generated rather than stored.
    /// </summary>
    private Task<List<string>> GetColumns() => Read("""
        SELECT CONCAT_WS('|', TABLE_NAME, COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE,
                         COALESCE(COLUMN_DEFAULT, '-'), EXTRA)
        FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME <> '__Migrations'
        ORDER BY 1
        """);

    private Task<List<string>> GetIndexes() => Read("""
        SELECT CONCAT_WS('|', TABLE_NAME, INDEX_NAME, SEQ_IN_INDEX, COLUMN_NAME, NON_UNIQUE)
        FROM information_schema.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME <> '__Migrations'
        ORDER BY 1
        """);

    private Task<List<string>> GetForeignKeys() => Read("""
        SELECT CONCAT_WS('|', CONSTRAINT_NAME, TABLE_NAME, REFERENCED_TABLE_NAME)
        FROM information_schema.REFERENTIAL_CONSTRAINTS
        WHERE CONSTRAINT_SCHEMA = DATABASE()
        ORDER BY 1
        """);

    private Task<List<string>> GetRoutines() => Read("""
        SELECT CONCAT_WS('|', ROUTINE_TYPE, ROUTINE_NAME)
        FROM information_schema.ROUTINES
        WHERE ROUTINE_SCHEMA = DATABASE()
        ORDER BY 1
        """);

    private Task<List<string>> GetTriggers() => Read("""
        SELECT CONCAT_WS('|', TRIGGER_NAME, EVENT_OBJECT_TABLE, EVENT_MANIPULATION, ACTION_TIMING)
        FROM information_schema.TRIGGERS
        WHERE TRIGGER_SCHEMA = DATABASE()
        ORDER BY 1
        """);

    /// <summary>
    /// Wipes the database. Foreign key checks go off first, because dropping in dependency order is
    /// exactly the problem this test is about and here it does not need solving.
    /// </summary>
    private async Task DropEverything()
    {
        await using var connection = new MySqlConnection(this.ConnectionString);
        await connection.OpenAsync();

        await Execute(connection, "SET FOREIGN_KEY_CHECKS = 0");

        foreach (var (query, template) in new[]
                 {
                     ("""
                      SELECT TRIGGER_NAME FROM information_schema.TRIGGERS WHERE TRIGGER_SCHEMA = DATABASE()
                      """, "DROP TRIGGER IF EXISTS `{0}`"),
                     ("""
                      SELECT TABLE_NAME FROM information_schema.VIEWS WHERE TABLE_SCHEMA = DATABASE()
                      """, "DROP VIEW IF EXISTS `{0}`"),
                     ("""
                      SELECT ROUTINE_NAME FROM information_schema.ROUTINES
                      WHERE ROUTINE_SCHEMA = DATABASE() AND ROUTINE_TYPE = 'PROCEDURE'
                      """, "DROP PROCEDURE IF EXISTS `{0}`"),
                     ("""
                      SELECT ROUTINE_NAME FROM information_schema.ROUTINES
                      WHERE ROUTINE_SCHEMA = DATABASE() AND ROUTINE_TYPE = 'FUNCTION'
                      """, "DROP FUNCTION IF EXISTS `{0}`"),
                     ("""
                      SELECT TABLE_NAME FROM information_schema.TABLES
                      WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'SEQUENCE'
                      """, "DROP SEQUENCE IF EXISTS `{0}`"),
                     ("""
                      SELECT TABLE_NAME FROM information_schema.TABLES
                      WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE'
                      """, "DROP TABLE IF EXISTS `{0}`"),
                 })
        {
            foreach (var name in await ReadNames(connection, query))
                await Execute(connection, string.Format(template, name));
        }

        await Execute(connection, "SET FOREIGN_KEY_CHECKS = 1");
    }

    private static async Task<List<string>> ReadNames(MySqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            names.Add(reader.GetString(0));

        return names;
    }

    private static async Task Execute(MySqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
