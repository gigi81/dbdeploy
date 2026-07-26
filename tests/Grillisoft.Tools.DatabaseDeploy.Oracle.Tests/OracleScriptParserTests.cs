using System.IO.Abstractions.TestingHelpers;
using AwesomeAssertions;
using Xunit;

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

    [Fact]
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
    [Fact]
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
    [Fact]
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

    [Fact]
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
}
