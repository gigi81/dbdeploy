using Grillisoft.Tools.DatabaseDeploy.Contracts.Formatting;
using Grillisoft.Tools.DatabaseDeploy.Oracle.Formatting;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle.Tests.Formatting;

public class OracleFormatterTests
{
    private static readonly SqlFormatterOptions Options = SqlFormatterOptions.Default with { NewLine = "\n" };

    private static string Format(string sql)
    {
        var result = new OracleFormatter().Format(sql, Options);

        TestContext.Current?.OutputWriter.WriteLine(result.Sql);
        result.VerificationError.Should().BeNull();

        return result.Sql;
    }

    /// <summary>
    /// Raw string literals take the line endings of the source file, and this repository stores C#
    /// with CRLF. The formatter is asked for "\n" output, so the expectation has to be normalised or
    /// the test would only pass where the source happens to use LF.
    /// </summary>
    private static string Lf(string text) => text.Replace("\r\n", "\n");

    [Test]
    public void Format_ShouldIndentAProgramUnitBody()
    {
        var result = Format(
            "create or replace procedure p is begin if x not between 1 and 2 or y in (1,2) then " +
            "raise_application_error(-20205, 'nope'); end if; end p;");

        result.Should().Be(Lf(
            """
            CREATE OR REPLACE PROCEDURE p
            IS
            BEGIN
                IF x NOT BETWEEN 1 AND 2
                    OR y IN (1, 2) THEN
                    raise_application_error(-20205, 'nope');
                END IF;
            END p;

            """));
    }

    /// <summary>SQL*Plus directives are not SQL and have to reach the server exactly as written.</summary>
    [Test]
    public void Format_ShouldLeaveSqlPlusDirectivesAlone()
    {
        var result = Format("SET  LINESIZE  80\nREM   a note\nselect 1 from dual;");

        result.Should().StartWith("SET  LINESIZE  80\nREM   a note\n");
    }

    /// <summary>
    /// The blank lines of a hand-laid-out SQL*Plus header separate its sections, so they survive.
    /// Their trailing whitespace does not, the same as everywhere else in the output.
    /// </summary>
    [Test]
    public void Format_ShouldKeepTheBlankLinesOfASqlPlusHeader()
    {
        var result = Format("rem header line   \nrem\nSET ECHO OFF\n\nREM section title\n\nselect 1 from dual;");

        result.Should().Be(Lf(
            """
            rem header line
            rem
            SET ECHO OFF

            REM section title

            SELECT
                1
            FROM
                dual;

            """));
    }

    /// <summary>
    /// A SET that assigns is the SET clause of an UPDATE, not a SQL*Plus directive.
    /// </summary>
    [Test]
    public void Format_ShouldTreatAnAssigningSetAsAnUpdateClause()
    {
        var result = Format("update employees set salary = 1 where id = 2;");

        result.Should().Be(Lf(
            """
            UPDATE
                employees
            SET
                salary = 1
            WHERE
                id = 2;

            """));
    }

    /// <summary>The terminator belongs directly under the statement it closes.</summary>
    [Test]
    public void Format_ShouldPutTheSlashDirectlyAfterTheStatement()
    {
        var result = Format("alter trigger t disable;\n/\n");

        result.Should().Be("ALTER TRIGGER t DISABLE;\n/\n");
    }

    /// <summary>
    /// A trigger's event list reads INSERT OR UPDATE OR DELETE. Those are event names, and laying
    /// them out as statements would wreck the header.
    /// </summary>
    [Test]
    public void Format_ShouldKeepATriggerEventListOnOneLine()
    {
        var result = Format(
            "create or replace trigger t before insert or update or delete on employees begin p; end;");

        result.Should().StartWith("CREATE OR REPLACE TRIGGER t BEFORE INSERT OR UPDATE OR DELETE ON employees\nBEGIN");
    }

    /// <summary>
    /// <c>%TYPE</c> is an attribute reference, and spacing it out would not compile.
    /// </summary>
    [Test]
    public void Format_ShouldKeepAnAttributeReferenceTight()
    {
        Format("create procedure p (a job_history.employee_id%type) is begin null; end;")
            .Should().Contain("a job_history.employee_id%TYPE");
    }

    /// <summary>
    /// A parameter list too long for one line breaks out under the routine name, the way the
    /// checked-in scripts write it. A short one stays inline - see
    /// <see cref="Format_WhenAParameterListIsShort_ShouldKeepItInline"/>.
    /// </summary>
    [Test]
    public void Format_ShouldPutALongRoutineParameterListOnItsOwnLine()
    {
        var result = Format(
            "create or replace procedure add_job_history (p_emp_id job_history.employee_id%type, " +
            "p_start_date job_history.start_date%type, p_end_date job_history.end_date%type) " +
            "is begin null; end add_job_history;");

        result.Should().StartWith(Lf(
            """
            CREATE OR REPLACE PROCEDURE add_job_history
            (
                p_emp_id job_history.employee_id%TYPE,
                p_start_date job_history.start_date%TYPE,
                p_end_date job_history.end_date%TYPE
            )
            IS
            BEGIN
            """));
    }

    /// <summary>
    /// A name followed by a parenthesis closes up, the same rule that keeps <c>COUNT(*)</c> and
    /// <c>NVARCHAR(30)</c> together.
    /// </summary>
    [Test]
    public void Format_WhenAParameterListIsShort_ShouldKeepItInline()
    {
        Format("create procedure p (a number, b date) is begin null; end;")
            .Should().StartWith("CREATE PROCEDURE p(a NUMBER, b DATE)\nIS\n");
    }

    [Test]
    public void Format_ShouldBeIdempotent()
    {
        var once = Format("begin\nif a then b; else c; end if;\nend;\n/\n");

        Format(once).Should().Be(once);
    }
}
