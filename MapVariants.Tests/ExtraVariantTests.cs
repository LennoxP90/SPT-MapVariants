using MapVariants.Shared;
using Xunit;

namespace MapVariants.Tests;

public class ExtraVariantTests
{
    static ManagedMap Factory() => new(
        new[] { "factory4_day", "factory4_night" }, null, "Classic", "Vanilla", "classic.png", "vanilla.png",
        displayName: "Factory",
        extras: new[] { new ExtraVariant(" Test-Halloween ", "Halloween 2026", "halloween.png") });

    [Fact]
    public void VariantsAreOriginalBackportThenExtrasInOrder()
        => Assert.Equal(new[] { "original", "backport", "test-halloween" }, Factory().Variants);

    [Fact]
    public void ConstructingTheMapDeclaresItsExtras()
    {
        Factory();
        Assert.Equal("test-halloween", MapVariant.Normalise("TEST-HALLOWEEN"));
    }

    [Fact]
    public void NamesAndTilesResolvePerVariant()
    {
        var map = Factory();
        Assert.Equal("Factory - Halloween 2026", VariantDisplay.For(map, "test-halloween"));
        Assert.Equal("Factory - Classic", VariantDisplay.For(map, "backport"));
        Assert.Equal("halloween.png", map.TileFor("test-halloween"));
        Assert.Equal("vanilla.png", map.TileFor("original"));
    }

    [Theory]
    [InlineData("Halloween 2026", "test-halloween")]
    [InlineData("halloween 2026", "test-halloween")]
    [InlineData("Classic", "backport")]
    [InlineData("Vanilla", "original")]
    [InlineData("nonsense", "backport")]
    public void DefaultSelectionReadsEveryVariantName(string stored, string expected)
        => Assert.Equal(expected, DefaultSelection.ToVariant(stored, Factory()));

    [Fact]
    public void ReservedAndDuplicateExtrasAreDropped()
    {
        var map = new ManagedMap(new[] { "bigmap" }, null, "B", "O", extras: new[]
        {
            new ExtraVariant("backport", "Shadow"), new ExtraVariant("", "Blank"),
            new ExtraVariant("test-dupe", "One"), new ExtraVariant("TEST-DUPE", "Two"),
        });
        Assert.Equal(new[] { "original", "backport", "test-dupe" }, map.Variants);
        Assert.Equal("One", map.NameFor("test-dupe"));
    }

    [Fact]
    public void WithTilesKeepsTheExtras()
        => Assert.Equal(Factory().Variants, Factory().WithTiles("a", "b").Variants);
}
