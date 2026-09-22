using System.IO.Abstractions.TestingHelpers;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Tests;
using Grillisoft.Tools.DatabaseDeploy.Tests.Databases;
using Oracle.ManagedDataAccess.Client;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle.Tests;

[InheritsTests]
[ClassDataSource<OracleFixture>(Shared = SharedType.PerAssembly)]
public class OracleDatabaseTests : DatabaseTest<OracleDatabase>
{
    public OracleDatabaseTests(OracleFixture fixture)
        : base(fixture)
    {
    }

    protected override IDictionary<string, string?> GetConfigurationSettings()
    {
        var ret = base.GetConfigurationSettings();
        ret.Add("databases:test:schema", "oracle");
        ret.Add("databases:test:migrationTable", "MIGRATIONS");
        return ret;
    }

    protected override IDatabaseFactory CreateDatabaseFactory()
    {
        return new OracleDatabaseFactory(
            new OracleScriptParser(),
            this.GlobalSettingsOptions,
            this.LoggerFactory);
    }

    protected override string ProviderName => OracleDatabaseFactory.ProviderName;

    /// <summary>
    /// A deploy script written the way people write them: block comments in a view and in a package
    /// body, inline, trailing and over several lines, holding the semicolons, apostrophes and even
    /// the lone slash that comments tend to hold. A block comment ends with a slash, and the parser
    /// used to take that for the end of the statement.
    /// </summary>
    private const string CommentedScript = """
        /* Objects that check a comment never cuts a statement short. */
        CREATE TABLE cmt_items (
          id   NUMBER PRIMARY KEY, /* the key */
          name VARCHAR2(100)       -- what it's called
        )
        /

        INSERT INTO cmt_items VALUES (1, 'it''s /* not a comment */ -- nor this');
        /

        CREATE OR REPLACE VIEW v_cmt_items AS
        SELECT id, /* inline */ name
        FROM cmt_items /* trailing */
        /

        CREATE OR REPLACE PACKAGE pkg_cmt AS
          /* the package's only function */
          FUNCTION greet RETURN VARCHAR2;
        END pkg_cmt;
        /

        CREATE OR REPLACE PACKAGE BODY pkg_cmt AS
          /*
           * A comment over several lines; with semicolons, an apostrophe that's
           * unpaired, and a line that is nothing but a slash:
           /
           */
          FUNCTION greet RETURN VARCHAR2 IS
          BEGIN
            RETURN 'hello'; /* the only answer */
          END greet; -- done
        END pkg_cmt;
        /

        /* That's all. */
        """;

    [Test]
    [Category(TestCategories.Docker)]
    public async Task RunScript_WhenAScriptHoldsBlockComments_ShouldDeployEveryStatementWhole(CancellationToken cancellationToken)
    {
        // arrange
        var sut = await this.CreateDatabase(cancellationToken);
        await DropCommentedObjects();

        var fileSystem = new MockFileSystem();
        fileSystem.AddFile("/scripts/TKT-001.Deploy.sql", new MockFileData(CommentedScript));

        try
        {
            // act: what ScriptsRunner does for every deploy script
            var executed = 0;
            await foreach (var statement in sut.ScriptParser.Parse(fileSystem.FileInfo.New("/scripts/TKT-001.Deploy.sql"), cancellationToken))
            {
                await sut.RunScript(statement, cancellationToken);
                executed++;
            }

            // assert
            executed.Should().Be(5, "the table, the insert, the view, the package and its body");

            (await Read("""
                SELECT object_type || ' ' || object_name || ' ' || status
                FROM user_objects
                WHERE object_name IN ('CMT_ITEMS', 'V_CMT_ITEMS', 'PKG_CMT')
                ORDER BY 1
                """)).Should().Equal(
                "PACKAGE BODY PKG_CMT VALID",
                "PACKAGE PKG_CMT VALID",
                "TABLE CMT_ITEMS VALID",
                "VIEW V_CMT_ITEMS VALID");

            (await Read("SELECT pkg_cmt.greet FROM dual")).Should().Equal("hello");

            // the comment markers inside the literal were data, not comments
            (await Read("SELECT name FROM v_cmt_items")).Should().Equal("it's /* not a comment */ -- nor this");
        }
        finally
        {
            await DropCommentedObjects();
        }
    }

    private async Task DropCommentedObjects()
    {
        foreach (var drop in new[] { "DROP VIEW v_cmt_items", "DROP PACKAGE pkg_cmt", "DROP TABLE cmt_items PURGE" })
        {
            try
            {
                await Execute(drop);
            }
            catch (OracleException ex) when (ex.Number is 942 or 4043)
            {
                // ORA-00942 / ORA-04043: nothing to drop
            }
        }
    }

    private async Task Execute(string sql)
    {
        await using var connection = new OracleConnection(this.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<List<string>> Read(string sql)
    {
        await using var connection = new OracleConnection(this.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(reader.GetString(0));

        return result;
    }
}
