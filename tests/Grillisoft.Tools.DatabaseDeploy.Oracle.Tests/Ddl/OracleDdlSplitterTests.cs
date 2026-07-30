using Grillisoft.Tools.DatabaseDeploy.Oracle.Ddl;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle.Tests.Ddl;

public class OracleDdlSplitterTests
{
    [Test]
    public void Split_WhenDdlIsASingleStatement_ShouldReturnIt()
    {
        var ddl = """

              CREATE TABLE "CUSTOMER"
               (	"ID" NUMBER(*,0) NOT NULL ENABLE,
            	"NAME" VARCHAR2(100)
               )
            """;

        var statements = OracleDdlSplitter.Split(ddl, isPlSql: false);

        statements.Should().ContainSingle()
                  .Which.Should().StartWith("CREATE TABLE \"CUSTOMER\"").And.EndWith(")");
    }

    /// <summary>
    /// A table often comes back as a CREATE followed by the constraints Oracle would not inline.
    /// Each one has to be sent to the server separately.
    /// </summary>
    [Test]
    public void Split_WhenDdlHoldsSeveralStatements_ShouldSplitThem()
    {
        var ddl = """
            CREATE TABLE "ORDER"
               (	"ID" NUMBER(*,0)
               )
            ALTER TABLE "ORDER" ADD CONSTRAINT "PK_ORDER" PRIMARY KEY ("ID") ENABLE
            CREATE UNIQUE INDEX "UX_ORDER" ON "ORDER" ("ID")
            """;

        var statements = OracleDdlSplitter.Split(ddl, isPlSql: false);

        statements.Should().HaveCount(3);
        statements[0].Should().StartWith("CREATE TABLE");
        statements[1].Should().StartWith("ALTER TABLE");
        statements[2].Should().StartWith("CREATE UNIQUE INDEX");
    }

    /// <summary>
    /// The old implementation searched for "ALTER TABLE" anywhere in the text, which cut a table in
    /// half whenever those words appeared inside a literal.
    /// </summary>
    [Test]
    public void Split_WhenKeywordIsInsideALiteral_ShouldNotSplit()
    {
        var ddl = """
            CREATE TABLE "AUDIT"
               (	"ID" NUMBER(*,0),
            	"MESSAGE" VARCHAR2(400) DEFAULT 'run this:
            ALTER TABLE "X" ADD y NUMBER
            to fix it'
               )
            """;

        var statements = OracleDdlSplitter.Split(ddl, isPlSql: false);

        statements.Should().ContainSingle();
    }

    [Test]
    public void Split_WhenKeywordIsInsideAComment_ShouldNotSplit()
    {
        var ddl = """
            CREATE TABLE "T1"
               (	"ID" NUMBER(*,0)
            /* historically this was
               ALTER TABLE "T1" ADD ...
               */
               )
            """;

        var statements = OracleDdlSplitter.Split(ddl, isPlSql: false);

        statements.Should().ContainSingle();
    }

    [Test]
    public void Split_WhenEscapedQuotesAreUsed_ShouldTrackTheLiteralCorrectly()
    {
        var ddl = """
            CREATE TABLE "T1"
               (	"LABEL" VARCHAR2(20) DEFAULT 'it''s here'
               )
            ALTER TABLE "T1" ADD CONSTRAINT "CK_T1" CHECK ("LABEL" IS NOT NULL) ENABLE
            """;

        var statements = OracleDdlSplitter.Split(ddl, isPlSql: false);

        statements.Should().HaveCount(2);
    }

    /// <summary>
    /// PL/SQL is full of statement keywords at the start of a line and must be kept whole,
    /// trailing END; included.
    /// </summary>
    [Test]
    public void Split_WhenObjectIsPlSql_ShouldNeverSplitAndShouldKeepTheTrailingSemicolon()
    {
        var ddl = """

              CREATE OR REPLACE PROCEDURE "REBUILD" AS
              BEGIN
                EXECUTE IMMEDIATE 'ALTER TABLE t MOVE';
                INSERT INTO log VALUES ('done');
              END;

            """;

        var statements = OracleDdlSplitter.Split(ddl, isPlSql: true);

        statements.Should().ContainSingle()
                  .Which.Should().StartWith("CREATE OR REPLACE PROCEDURE").And.EndWith("END;");
    }

    /// <summary>
    /// DBMS_METADATA appends the enable statement to a trigger body; sending the two together
    /// fails with ORA-24344.
    /// </summary>
    [Test]
    public void Split_WhenPlSqlEndsWithAnAlter_ShouldPeelItOff()
    {
        var ddl = """
            CREATE OR REPLACE TRIGGER "TRG1"
            BEFORE INSERT ON orders
            FOR EACH ROW
            BEGIN
              :NEW.total := 0;
            END;
            ALTER TRIGGER "TRG1" ENABLE
            """;

        var statements = OracleDdlSplitter.Split(ddl, isPlSql: true);

        statements.Should().HaveCount(2);
        statements[0].Should().StartWith("CREATE OR REPLACE TRIGGER").And.EndWith("END;");
        statements[1].Should().Be("ALTER TRIGGER \"TRG1\" ENABLE");
    }

    /// <summary>
    /// An ALTER inside the body is part of the program unit and must be left alone.
    /// </summary>
    [Test]
    public void Split_WhenPlSqlHoldsAnAlterInItsBody_ShouldKeepItWhole()
    {
        var ddl = """
            CREATE OR REPLACE PROCEDURE "P1" AS
            BEGIN
            EXECUTE IMMEDIATE 'ALTER TABLE t MOVE';
            END;
            """;

        var statements = OracleDdlSplitter.Split(ddl, isPlSql: true);

        statements.Should().ContainSingle().Which.Should().EndWith("END;");
    }

    [Test]
    public void Split_ShouldStripTheStatementTerminator()
    {
        var statements = OracleDdlSplitter.Split("CREATE SEQUENCE \"S1\" START WITH 1;\n", isPlSql: false);

        statements.Should().ContainSingle()
                  .Which.Should().Be("CREATE SEQUENCE \"S1\" START WITH 1");
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   \n\n  ")]
    public void Split_WhenThereIsNothingToSplit_ShouldReturnEmpty(string? ddl)
    {
        OracleDdlSplitter.Split(ddl, isPlSql: false).Should().BeEmpty();
        OracleDdlSplitter.Split(ddl, isPlSql: true).Should().BeEmpty();
    }
}
