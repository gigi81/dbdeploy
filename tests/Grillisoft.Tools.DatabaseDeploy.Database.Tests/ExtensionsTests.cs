
namespace Grillisoft.Tools.DatabaseDeploy.Database.Tests;

public class ExtensionsTests
{
    /// <summary>
    /// The breakdown is read straight off a log line to judge whether a generation did what it was
    /// supposed to, so the order has to be the order of the script, not the order of the input.
    /// </summary>
    [Test]
    public void Breakdown_ShouldCountByTypeInScriptOrder()
    {
        // Arrange
        string[] types = ["VIEW", "TABLE", "VIEW", "SCHEMA", "TABLE", "TABLE"];
        var rank = (string type) => type switch { "SCHEMA" => 0, "TABLE" => 1, "VIEW" => 2, _ => int.MaxValue };

        // Act
        var result = types.Breakdown(rank);

        // Assert
        result.Should().Be("SCHEMA (1), TABLE (3), VIEW (2)");
    }

    [Test]
    public void Breakdown_WhenCountsAreAlreadyTotalled_ShouldFormatThemTheSameWay()
    {
        // Arrange
        var counts = new Dictionary<string, int> { ["VIEW"] = 2, ["TABLE"] = 3 };
        var rank = (string type) => type switch { "TABLE" => 1, "VIEW" => 2, _ => int.MaxValue };

        // Act
        var result = counts.Breakdown(rank);

        // Assert
        result.Should().Be("TABLE (3), VIEW (2)");
    }

    /// <summary>An unknown type must not push a known one down the list.</summary>
    [Test]
    public void Breakdown_WhenATypeHasNoRank_ShouldPutItLast()
    {
        // Arrange
        string[] types = ["SOMETHING NEW", "TABLE"];
        var rank = (string type) => type == "TABLE" ? 1 : int.MaxValue;

        // Act
        var result = types.Breakdown(rank);

        // Assert
        result.Should().Be("TABLE (1), SOMETHING NEW (1)");
    }

    [Test]
    public void Breakdown_WhenThereIsNothingToCount_ShouldBeEmpty()
    {
        Array.Empty<string>().Breakdown(_ => 0).Should().BeEmpty();
    }
}
