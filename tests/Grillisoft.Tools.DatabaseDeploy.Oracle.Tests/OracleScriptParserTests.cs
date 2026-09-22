using System.IO.Abstractions.TestingHelpers;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle.Tests;

public class OracleScriptParserTests
{
    private static async Task<List<string>> Parse(string script)
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile("/scripts/init.sql", new MockFileData(script));

        var parser = new OracleScriptParser();
        var commands = new List<string>();

        await foreach (var command in parser.Parse(fileSystem.FileInfo.New("/scripts/init.sql"), CancellationToken.None))
            commands.Add(command);

        return commands;
    }

    [Test]
    public async Task Parse_ShouldSplitStatementsOnTheirTerminator()
    {
        var commands = await Parse("""
            CREATE TABLE "T1" ("ID" NUMBER)
            /

            CREATE TABLE "T2" ("ID" NUMBER)
            /

            """);

        commands.Should().HaveCount(2);
        commands[0].Should().Be("CREATE TABLE \"T1\" (\"ID\" NUMBER)");
        commands[1].Should().Be("CREATE TABLE \"T2\" (\"ID\" NUMBER)");
    }

    /// <summary>
    /// The header dbdeploy writes at the top of a generated script must never reach the server.
    /// </summary>
    [Test]
    public async Task Parse_ShouldIgnoreRemComments()
    {
        var commands = await Parse("""
            REM ==========================
            REM Schema HR - 1 object(s)
            REM ==========================

            CREATE TABLE "T1" ("ID" NUMBER)
            /

            REM !! SOME_PKG---PACKAGE BODY could not be scripted: ORA-31603

            """);

        commands.Should().ContainSingle()
                .Which.Should().Be("CREATE TABLE \"T1\" (\"ID\" NUMBER)");
    }

    /// <summary>
    /// An apostrophe in a PL/SQL comment or an odd number of quotes anywhere used to leave the
    /// parser convinced it was still inside a string literal, so it swallowed every statement that
    /// followed. A lone "/" ends the statement the way SQL*Plus does, whatever the quotes did.
    /// </summary>
    [Test]
    public async Task Parse_WhenAStatementHoldsAnOddNumberOfQuotes_ShouldStillTerminateOnALoneSlash()
    {
        var commands = await Parse("""
            CREATE OR REPLACE PROCEDURE "P1" AS
            BEGIN
              -- don't touch this without asking
              NULL;
            END;
            /

            CREATE TABLE "T1" ("ID" NUMBER)
            /

            """);

        commands.Should().HaveCount(2);
        commands[0].Should().StartWith("CREATE OR REPLACE PROCEDURE").And.EndWith("END;");
        commands[1].Should().Be("CREATE TABLE \"T1\" (\"ID\" NUMBER)");
    }

    [Test]
    public async Task Parse_ShouldKeepQuotedSemicolonsInsideTheStatement()
    {
        var commands = await Parse("""
            CREATE OR REPLACE PROCEDURE "P1" AS
            BEGIN
              INSERT INTO log VALUES ('one; two');
            END;
            /

            """);

        commands.Should().ContainSingle()
                .Which.Should().Contain("'one; two'").And.EndWith("END;");
    }

    /// <summary>
    /// A block comment ends with a slash, so a line closing one used to read as the end of the
    /// statement: the view was sent cut off at its comment, and the rest of it went out as a
    /// statement of its own.
    /// </summary>
    [Test]
    public async Task Parse_WhenAViewHoldsABlockComment_ShouldKeepItWhole()
    {
        var commands = await Parse("""
            CREATE OR REPLACE VIEW "V1" AS
            SELECT "ID" /* the key */
            FROM "T1"
            /

            CREATE TABLE "T2" ("ID" NUMBER)
            /

            """);

        commands.Should().HaveCount(2);
        commands[0].Should().StartWith("CREATE OR REPLACE VIEW").And.Contain("/* the key */").And.EndWith("FROM \"T1\"");
        commands[1].Should().Be("CREATE TABLE \"T2\" (\"ID\" NUMBER)");
    }

    [Test]
    public async Task Parse_WhenAPackageBodyHoldsBlockComments_ShouldKeepItWhole()
    {
        var commands = await Parse("""
            CREATE OR REPLACE PACKAGE BODY "PKG" AS
              /* Everything in here is the package's own. */
              PROCEDURE "RUN" IS
              BEGIN
                NULL; /* nothing to do yet */
              END "RUN";
              /*
               * A comment over several lines; with a semicolon, an apostrophe that's unpaired,
               * and a line that is nothing but a slash:
               /
               */
            END "PKG";
            /

            CREATE TABLE "T1" ("ID" NUMBER)
            /

            """);

        commands.Should().HaveCount(2);
        commands[0].Should().StartWith("CREATE OR REPLACE PACKAGE BODY").And.EndWith("END \"PKG\";");
        commands[1].Should().Be("CREATE TABLE \"T1\" (\"ID\" NUMBER)");
    }

    /// <summary>
    /// The terminator used to be picked by whether any line started with a slash, which a line
    /// opening a block comment does. A script of semicolon terminated statements then had none of
    /// them split.
    /// </summary>
    [Test]
    public async Task Parse_WhenASemicolonScriptOpensWithABlockComment_ShouldStillSplitOnSemicolons()
    {
        var commands = await Parse("""
            /* Seed data */
            INSERT INTO "T1" VALUES (1);
            INSERT INTO "T1" VALUES (2);

            """);

        commands.Should().HaveCount(2);
        commands[0].Should().Be("/* Seed data */\nINSERT INTO \"T1\" VALUES (1)".ReplaceLineEndings());
        commands[1].Should().Be("INSERT INTO \"T1\" VALUES (2)");
    }

    [Test]
    public async Task Parse_WhenAStatementIsFollowedByAComment_ShouldStripTheTerminatorAheadOfIt()
    {
        var commands = await Parse("""
            INSERT INTO "T1" VALUES (1); /* the first */
            INSERT INTO "T1" VALUES (2); -- the second

            """);

        commands.Should().HaveCount(2);
        commands[0].Should().Be("INSERT INTO \"T1\" VALUES (1) /* the first */");
        commands[1].Should().Be("INSERT INTO \"T1\" VALUES (2) -- the second");
    }

    /// <summary>
    /// A comment with no statement after it is still a command as far as the file goes, and the
    /// server answers one with ORA-00900.
    /// </summary>
    [Test]
    public async Task Parse_WhenTheScriptEndsWithAComment_ShouldNotSendIt()
    {
        var commands = await Parse("""
            CREATE TABLE "T1" ("ID" NUMBER)
            /

            /* That's all. */
            -- Really.

            """);

        commands.Should().ContainSingle()
                .Which.Should().Be("CREATE TABLE \"T1\" (\"ID\" NUMBER)");
    }

    /// <summary>
    /// A q-quoted literal needs no escaping, so it is where an apostrophe or a comment marker is
    /// most likely to turn up inside a string.
    /// </summary>
    [Test]
    public async Task Parse_WhenAQQuotedLiteralHoldsCommentMarkers_ShouldNotTreatThemAsComments()
    {
        var commands = await Parse("""
            INSERT INTO "T1" VALUES (q'[it's /* not a comment]');
            INSERT INTO "T1" VALUES (q'{-- nor this; */}');

            """);

        commands.Should().HaveCount(2);
        commands[0].Should().Be("INSERT INTO \"T1\" VALUES (q'[it's /* not a comment]')");
        commands[1].Should().Be("INSERT INTO \"T1\" VALUES (q'{-- nor this; */}')");
    }
}
