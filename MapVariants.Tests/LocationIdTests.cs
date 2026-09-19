using MapVariants.Shared;
using Xunit;

namespace MapVariants.Tests;

public class LocationIdTests
{
    [Theory]
    [InlineData("lighthouse", "lighthouse")]
    [InlineData("Lighthouse", "lighthouse")]
    [InlineData("  INTERCHANGE  ", "interchange")]
    [InlineData("factory4_day", "factory4_day")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void TrimmedAndLowerCased(string? input, string expected)
        => Assert.Equal(expected, LocationId.Normalise(input));

    // The routes, the raid hooks and both stores key the same dictionaries, so they must agree.
    [Fact]
    public void ManagedMapUsesTheSameNormalisation()
    {
        Assert.Equal(LocationId.Normalise(" Lighthouse "), ManagedMap.Normalise(" Lighthouse "));
        Assert.Equal(LocationId.Normalise(null), ManagedMap.Normalise(null));
    }
}
