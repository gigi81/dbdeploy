using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.SqlServer.Formatting;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer.Tests.Formatting;

/// <summary>
/// The layout contract for T-SQL. Every case feeds in a deliberately badly laid out script and
/// pins the exact output, so a change in the emitter has to be an explicit decision.
/// </summary>
public class SqlServerFormatterTests
{
    private static readonly SqlFormatterOptions Options = SqlFormatterOptions.Default with { NewLine = "\n" };

    private static string Format(string sql, SqlFormatterOptions? options = null)
    {
        var result = new SqlServerFormatter().Format(sql, options ?? Options);

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
    public void Format_ShouldPutEachClauseOnItsOwnLineWithTheBodyIndented()
    {
        var result = Format(
            "select Field1, Field2 from TABLE1 t1 inner join TABLE2 t2 on t1.id = t2.id " +
            "where Field1 = 'a' and Field2 = 'b'");

        result.Should().Be(Lf(
            """
            SELECT
                Field1,
                Field2
            FROM
                TABLE1 t1
                INNER JOIN TABLE2 t2 ON t1.id = t2.id
            WHERE
                Field1 = 'a'
                AND Field2 = 'b'

            """));
    }

    [Test]
    public void Format_ShouldKeepAShortParenthesisedGroupInline()
    {
        var result = Format("select count(*) from Customers where Country in ('IT', 'FR', 'DE')");

        result.Should().Be(Lf(
            """
            SELECT
                COUNT(*)
            FROM
                Customers
            WHERE
                Country IN ('IT', 'FR', 'DE')

            """));
    }

    [Test]
    public void Format_ShouldPutTheColumnListOfACreateOnItsOwnLine()
    {
        var result = Format(
            "create table dbo.Customers (CustomerId int not null identity(1,1), " +
            "FirstName nvarchar(30) null, constraint PK_Customers primary key clustered (CustomerId))");

        result.Should().Be(Lf(
            """
            CREATE TABLE dbo.Customers
            (
                CustomerId INT NOT NULL IDENTITY(1, 1),
                FirstName NVARCHAR(30) NULL,
                CONSTRAINT PK_Customers PRIMARY KEY CLUSTERED (CustomerId)
            )

            """));
    }

    [Test]
    public void Format_ShouldKeepTheBatchSeparatorOnItsOwnLine()
    {
        var result = Format("alter table Customers drop column ContactName\ngo");

        result.Should().Be(Lf(
            """
            ALTER TABLE Customers DROP COLUMN ContactName
            GO

            """));
    }

    /// <summary>An identifier must never be re-cased, however the author wrote it.</summary>
    [Test]
    public void Format_ShouldLeaveIdentifiersAlone()
    {
        var result = Format("select [myColumn], MyOther from [dbo].[myTable]");

        result.Should().Contain("[myColumn]").And.Contain("MyOther").And.Contain("[dbo].[myTable]");
    }

    [Test]
    public void Format_ShouldPreserveComments()
    {
        var result = Format("-- keep me\nselect 1 /* and me */");

        result.Should().Contain("-- keep me").And.Contain("/* and me */");
    }

    /// <summary>
    /// A blank line the author left in front of a comment or a statement separates them, so one is
    /// kept. Longer runs collapse to <c>blank_lines_between_statements</c>, and a blank line in the
    /// middle of a statement goes: that layout is rebuilt from the tokens.
    /// </summary>
    [Test]
    public void Format_ShouldKeepOneBlankLineBeforeCommentsAndStatements()
    {
        var result = Format("-- section a\n\n-- section b\n\n\nselect 1\n\n, 2 from T");

        result.Should().Be(Lf(
            """
            -- section a

            -- section b

            SELECT
                1,
                2
            FROM
                T

            """));
    }

    [Test]
    public void Format_ShouldBeIdempotent()
    {
        const string Sql =
            "select a, b from T1 join T2 on T1.id = T2.id where a > 1 and b < 2 order by a";

        var once = Format(Sql);
        var twice = Format(once);

        twice.Should().Be(once);
    }

    [Test]
    public void Format_ShouldUseTheConfiguredIndentAndNewLine()
    {
        var result = Format(
            "select a from T",
            Options with { Indent = "\t", NewLine = "\r\n" });

        result.Should().Be("SELECT\r\n\ta\r\nFROM\r\n\tT\r\n");
    }

    [Test]
    public void Format_WhenDisabled_ShouldReturnTheScriptUntouched()
    {
        const string Sql = "select    a   from T";

        Format(Sql, Options with { Enabled = false }).Should().Be(Sql);
    }
}
