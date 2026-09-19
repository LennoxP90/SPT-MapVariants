using MapVariants.Shared;
using Xunit;

namespace MapVariants.Tests;

public class MapVariantTests
{
    [Theory]
    [InlineData("backport", "backport")]
    [InlineData("BACKPORT", "backport")]
    [InlineData(" backport ", "backport")]
    [InlineData("original", "original")]
    [InlineData("vanilla", "original")]
    [InlineData("", "original")]
    [InlineData(null, "original")]
    [InlineData("rework", "original")]
    public void AnythingThatIsNotBackportIsOriginal(string? input, string expected)
        => Assert.Equal(expected, MapVariant.Normalise(input));

    [Fact]
    public void IsBackportFollowsNormalise()
    {
        Assert.True(MapVariant.IsBackport("Backport"));
        Assert.False(MapVariant.IsBackport("classic"));
    }

    [Fact]
    public void UnmanagedIsItsOwnWireValue()
        => Assert.Equal("unmanaged", MapVariant.Unmanaged);

    // A route answering 'unmanaged' still normalises to 'original', so nothing downstream shifts.
    [Fact]
    public void UnmanagedNormalisesToOriginalLikeAnyOtherUnknownString()
    {
        Assert.Equal(MapVariant.Original, MapVariant.Normalise(MapVariant.Unmanaged));
        Assert.False(MapVariant.IsBackport(MapVariant.Unmanaged));
    }

    [Theory]
    [InlineData("unmanaged", true)]
    [InlineData("UNMANAGED", true)]
    [InlineData(" unmanaged ", true)]
    [InlineData("original", false)]
    [InlineData("backport", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsUnmanagedRecognisesOnlyTheSentinel(string? input, bool expected)
        => Assert.Equal(expected, MapVariant.IsUnmanaged(input));
}
