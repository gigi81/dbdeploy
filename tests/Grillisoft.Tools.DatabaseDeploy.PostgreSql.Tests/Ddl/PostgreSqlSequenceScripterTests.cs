using Grillisoft.Tools.DatabaseDeploy.PostgreSql.Ddl;

namespace Grillisoft.Tools.DatabaseDeploy.PostgreSql.Tests.Ddl;

public class PostgreSqlSequenceScripterTests
{
    private static PostgreSqlSequenceDefinition Ascending(
        string type = "bigint",
        long start = 1,
        long increment = 1,
        long? minimum = null,
        long? maximum = null,
        long cache = 1,
        bool cycle = false)
        => new(type, start, increment, minimum ?? 1, maximum ?? MaximumOf(type), cache, cycle);

    private static long MaximumOf(string type) => type switch
    {
        "smallint" => short.MaxValue,
        "integer" => int.MaxValue,
        _ => long.MaxValue,
    };

    private static string Render(PostgreSqlSequenceDefinition sequence)
        => PostgreSqlSequenceScripter.Render(sequence, "app", "order_seq")
            .Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>
    /// A bound equal to the default is written as NO MINVALUE / NO MAXVALUE rather than spelled
    /// out, so a sequence of a different type replays with that type's own defaults.
    /// </summary>
    [Test]
    public void Render_WhenEverythingIsDefault_ShouldSayNoBounds()
    {
        Render(Ascending()).Should().Be("""
            CREATE SEQUENCE "app"."order_seq"
                START WITH 1
                INCREMENT BY 1
                NO MINVALUE
                NO MAXVALUE
            """.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    /// <summary>bigint is the default type and saying so would only be noise.</summary>
    [Test]
    public void Render_ShouldWriteTheTypeOnlyWhenItIsNotBigint()
    {
        Render(Ascending()).Should().NotContain("AS bigint");
        Render(Ascending("integer")).Should().Contain("AS integer");
    }

    [Test]
    public void Render_ShouldWriteBoundsThatAreNotTheDefault()
    {
        var sql = Render(Ascending(start: 100, increment: 5, minimum: 10, maximum: 1000, cache: 20, cycle: true));

        sql.Should().Contain("START WITH 100")
           .And.Contain("INCREMENT BY 5")
           .And.Contain("MINVALUE 10")
           .And.Contain("MAXVALUE 1000")
           .And.Contain("CACHE 20")
           .And.Contain("CYCLE");
    }

    /// <summary>
    /// A descending sequence's defaults are the other way round: its minimum is its type's floor
    /// and its maximum is -1.
    /// </summary>
    [Test]
    public void Render_WhenTheSequenceDescends_ShouldUseTheOppositeDefaults()
    {
        var descending = new PostgreSqlSequenceDefinition("bigint", -1, -1, long.MinValue, -1, 1, false);

        Render(descending).Should().Contain("NO MINVALUE").And.Contain("NO MAXVALUE");
    }

    /// <summary>
    /// Where a sequence has got to is data, and this is a schema script: a setval in it would make
    /// the file differ on every run and replay a value that means nothing elsewhere.
    /// </summary>
    [Test]
    public void Render_ShouldNeverWriteSetval()
    {
        Render(Ascending(start: 500)).Should().NotContain("setval");
    }

    [Test]
    public void RenderIdentityOptions_WhenTheSequenceIsDefault_ShouldBeEmpty()
    {
        PostgreSqlSequenceScripter.RenderIdentityOptions(Ascending("integer")).Should().BeEmpty();
    }

    [Test]
    public void RenderIdentityOptions_ShouldWriteOnlyWhatDiffersFromTheDefault()
    {
        var sequence = Ascending("integer", start: 100, increment: 5);

        PostgreSqlSequenceScripter.RenderIdentityOptions(sequence)
            .Should().Be(" ( INCREMENT BY 5 START WITH 100 )");
    }
}
