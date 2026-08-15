using Grillisoft.Tools.DatabaseDeploy.PostgreSql.Ddl;

namespace Grillisoft.Tools.DatabaseDeploy.PostgreSql.Tests.Ddl;

public class PostgreSqlTypeScripterTests
{
    private static string Render(PostgreSqlTypeDefinition type)
        => PostgreSqlTypeScripter.Render(type).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static PostgreSqlTypeDefinition Enum(params string[] labels)
        => new("app", "mood", 'e', labels, [], "", false, "", []);

    [Test]
    public void Render_ShouldWriteAnEnumWithItsLabelsInOrder()
    {
        Render(Enum("sad", "ok", "happy")).Should().Be("""
            CREATE TYPE "app"."mood" AS ENUM (
                'sad',
                'ok',
                'happy'
            )
            """.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    /// <summary>A label is data and can hold anything, an apostrophe included.</summary>
    [Test]
    public void Render_ShouldEscapeALabelHoldingAQuote()
    {
        Render(Enum("it's fine")).Should().Contain("'it''s fine'");
    }

    [Test]
    public void Render_ShouldWriteACompositeWithItsAttributes()
    {
        var type = new PostgreSqlTypeDefinition("app", "address", 'c', [],
            [("street", "text"), ("city", "character varying(50)")], "", false, "", []);

        Render(type).Should().Be("""
            CREATE TYPE "app"."address" AS (
                "street" text,
                "city" character varying(50)
            )
            """.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Test]
    public void Render_ShouldWriteADomainWithItsBaseType()
    {
        var type = new PostgreSqlTypeDefinition("app", "positive", 'd', [], [], "integer", false, "", []);

        Render(type).Should().Be("CREATE DOMAIN \"app\".\"positive\" AS integer");
    }

    [Test]
    public void Render_ShouldWriteADomainsDefaultNotNullAndConstraints()
    {
        var type = new PostgreSqlTypeDefinition("app", "year", 'd', [], [], "integer", true, "1901",
            [("year_check", "CHECK (((VALUE >= 1901) AND (VALUE <= 2155)))")]);

        Render(type).Should().Be("""
            CREATE DOMAIN "app"."year" AS integer
                DEFAULT 1901
                NOT NULL
                CONSTRAINT "year_check" CHECK (((VALUE >= 1901) AND (VALUE <= 2155)))
            """.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    /// <summary>
    /// A range type's definition names an operator class and a handful of support functions, and
    /// getting one of them wrong produces a type that is quietly not the same. Failing as one
    /// object is reported in the script and costs nothing else.
    /// </summary>
    [Test]
    public void Render_WhenTheTypeIsOneItCannotBuild_ShouldSaySo()
    {
        var type = new PostgreSqlTypeDefinition("app", "period", 'r', [], [], "", false, "", []);

        var render = () => PostgreSqlTypeScripter.Render(type);

        render.Should().Throw<NotSupportedException>().WithMessage("*period*");
    }
}
