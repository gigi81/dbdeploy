using Grillisoft.Tools.DatabaseDeploy.Contracts.Formatting;
using Grillisoft.Tools.DatabaseDeploy.PostgreSql.Formatting;

namespace Grillisoft.Tools.DatabaseDeploy.PostgreSql.Tests.Formatting;

public class PostgreSqlFormatterTests
{
    private static readonly SqlFormatterOptions Options = SqlFormatterOptions.Default with { NewLine = "\n" };

    private static string Format(string sql)
    {
        var result = new PostgreSqlFormatter().Format(sql, Options);

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
    /// A dollar-quoted body is one literal. Reading it any other way would let the semicolons inside
    /// a function terminate the CREATE that contains it.
    /// </summary>
    [Test]
    public void Format_ShouldReadADollarQuotedBodyAsOneLiteral()
    {
        const string Body = "$$\n  BEGIN\n    RETURN 1;\n  END;\n$$";

        Format($"create function f() returns int as {Body} language plpgsql;")
            .Should().Contain(Body);
    }

    [Test]
    public void Format_ShouldReadATaggedDollarQuotedBodyAsOneLiteral()
    {
        const string Body = "$fn$ SELECT 'a;b'; $fn$";

        Format($"create function f() returns text as {Body} language sql;")
            .Should().Contain(Body);
    }

    [Test]
    public void Format_ShouldIndentTheArmsOfACase()
    {
        var result = Format(
            "select case when a then 'active'::text else ''::text end as notes from customer;");

        result.Should().Be(Lf(
            """
            SELECT
                CASE
                    WHEN a THEN 'active'::TEXT
                    ELSE ''::TEXT
                END AS notes
            FROM
                customer;

            """));
    }

    /// <summary>A group broken over lines can still be followed by the rest of the expression.</summary>
    [Test]
    public void Format_ShouldKeepAnAliasAfterAMultiLineGroup()
    {
        var result = Format(
            "select (select group_concat(f.title) from film f where f.id = 1 and f.other = 2 " +
            "and f.third = 3 group by f.id) as film_info from actor;");

        result.Should().Contain(") AS film_info");
    }

    [Test]
    public void Format_ShouldPutTheViewBodyAfterAsOnItsOwnLine()
    {
        Format("create view public.v as select a from t;")
            .Should().StartWith("CREATE VIEW public.v\nAS\nSELECT\n");
    }

    [Test]
    public void Format_ShouldBeIdempotent()
    {
        var once = Format("select a, b from t where a = 1 or b = 2 order by a;");

        Format(once).Should().Be(once);
    }
}
