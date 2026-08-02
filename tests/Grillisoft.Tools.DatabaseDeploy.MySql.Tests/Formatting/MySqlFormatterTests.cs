using Grillisoft.Tools.DatabaseDeploy.Contracts.Formatting;
using Grillisoft.Tools.DatabaseDeploy.MySql.Formatting;

namespace Grillisoft.Tools.DatabaseDeploy.MySql.Tests.Formatting;

public class MySqlFormatterTests
{
    private static readonly SqlFormatterOptions Options = SqlFormatterOptions.Default with { NewLine = "\n" };

    private static string Format(string sql)
    {
        var result = new MySqlFormatter().Format(sql, Options);

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

    /// <summary>
    /// While a custom delimiter is in force the semicolons inside a routine body are inner
    /// statements, and the delimiter itself closes the routine.
    /// </summary>
    [Test]
    public void Format_ShouldHonourACustomDelimiter()
    {
        var result = Format("delimiter //\ncreate function f() returns int begin return 1; end //\ndelimiter ;");

        result.Should().Be(Lf(
            """
            delimiter //
            CREATE FUNCTION f() RETURNS INT
            BEGIN
                RETURN 1;
            END
            //

            delimiter ;

            """));
    }

    /// <summary>
    /// The IF of DROP ... IF EXISTS belongs to the DROP; treating it as a procedural IF would open a
    /// block that never closes.
    /// </summary>
    [Test]
    public void Format_ShouldKeepDropIfExistsOnOneLine()
    {
        Format("drop function if exists emp_dept_id;")
            .Should().Be("DROP FUNCTION IF EXISTS emp_dept_id;\n");
    }

    [Test]
    public void Format_ShouldKeepTheLimitValueBesideTheKeyword()
    {
        var result = Format("select a from t where b = 1 limit 1;");

        result.Should().Be(Lf(
            """
            SELECT
                a
            FROM
                t
            WHERE
                b = 1
            LIMIT 1;

            """));
    }

    [Test]
    public void Format_ShouldPreserveBacktickIdentifiersAndHashComments()
    {
        var result = Format("select `to_date` from t # a note\n");

        result.Should().Contain("`to_date`").And.Contain("# a note");
    }

    /// <summary>A multi-line string literal is content and must survive byte for byte.</summary>
    [Test]
    public void Format_ShouldNotTouchAMultiLineStringLiteral()
    {
        const string Literal = "'\n    == USAGE ==\n    text\n'";

        Format($"create function f() returns text begin return {Literal}; end;")
            .Should().Contain(Literal);
    }

    [Test]
    public void Format_ShouldBeIdempotent()
    {
        var once = Format("delimiter //\ncreate procedure p() begin select 1; end //\ndelimiter ;");

        Format(once).Should().Be(once);
    }
}
