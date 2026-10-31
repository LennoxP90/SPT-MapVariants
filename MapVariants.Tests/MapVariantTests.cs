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

    // Ids are process-wide, so each test declares its own to stay independent of the others.
    [Fact]
    public void ADeclaredVariantSurvivesNormaliseAndIsNotVanilla()
    {
        Assert.True(MapVariant.Declare(" Test-Declared "));
        Assert.Equal("test-declared", MapVariant.Normalise("TEST-DECLARED"));
        Assert.True(MapVariant.IsBackport("test-declared"));
    }

    [Fact]
    public void AnUndeclaredVariantStillFoldsToOriginal()
    {
        Assert.Equal(MapVariant.Original, MapVariant.Normalise("never-declared"));
        Assert.False(MapVariant.IsBackport("never-declared"));
    }

    [Theory]
    [InlineData("original")]
    [InlineData("BACKPORT")]
    [InlineData("unmanaged")]
    [InlineData("")]
    [InlineData(null)]
    public void ReservedAndEmptyIdsCannotBeDeclared(string? id)
        => Assert.False(MapVariant.Declare(id));

    [Fact]
    public void DeclaringUnmanagedLeavesItFoldingToOriginal()
    {
        MapVariant.Declare(MapVariant.Unmanaged);
        Assert.Equal(MapVariant.Original, MapVariant.Normalise(MapVariant.Unmanaged));
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
