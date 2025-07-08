using FluentAssertions;
using Xunit;

namespace Grillisoft.Tools.DatabaseDeploy.Database.Tests;

public class ExtensionsTests
{
    [Theory]
    [InlineData("this is a test", "is", new[] { 2, 5 })]
    [InlineData("this is a test", "notfound", new int[0])]
    [InlineData("", "test", new int[0])]
    public void AllIndexes_WithStringSearch_ShouldReturnCorrectIndexes(string text, string search, int[] expected)
    {
        // Act
        var result = text.AllIndexes(search);

        // Assert
        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void AllIndexes_WithMultipleSearches_ShouldReturnCorrectIndexes()
    {
        // Arrange
        var text = "this is a test with multiple searches";
        var searches = new[] { "this", "test", "multiple" };
        var expected = new[] { 0, 10, 20 };

        // Act
        var result = text.AllIndexes(searches);

        // Assert
        result.Should().BeEquivalentTo(expected);
    }
}
