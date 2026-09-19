using MapVariants.Shared;
using Xunit;

namespace MapVariants.Tests;

public class ManagedMapSetTests
{
    static ManagedMapSet Set()
    {
        var set = new ManagedMapSet();
        set.Add(new ManagedMap(new[] { "lighthouse" }, "com.manimal.lighthouse", "Manimal", "Vanilla"));
        set.Add(new ManagedMap(new[] { "factory4_day", "factory4_night" }, null, "Classic", "Vanilla", displayName: "Factory"));
        return set;
    }

    [Theory]
    [InlineData("lighthouse", true)]
    [InlineData("LIGHTHOUSE", true)]
    [InlineData(" Lighthouse ", true)]
    [InlineData("factory4_night", true)]
    [InlineData("interchange", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ManagedIsByAnyOfTheMapsIds(string? id, bool expected)
        => Assert.Equal(expected, Set().IsManaged(id));

    [Fact]
    public void SeveralIdsResolveToOneMap()
    {
        var set = Set();
        Assert.Same(set.For("factory4_day"), set.For("factory4_night"));
        Assert.Equal("factory4_day", set.For("factory4_night")!.Key);
    }

    // The FactoryClassic lesson: a transit prompts only for the map it targets.
    [Theory]
    [InlineData("lighthouse", true)]
    [InlineData("bigmap", false)]
    [InlineData("woods", false)]
    [InlineData(null, false)]
    public void OnlyAManagedDestinationCanPrompt(string? destination, bool expected)
        => Assert.Equal(expected, Set().IsManagedDestination(destination));

    [Fact]
    public void DisplayNamesComeFromTheMap()
    {
        var map = Set().For("lighthouse")!;
        Assert.Equal("Lighthouse - Manimal", VariantDisplay.For(map, MapVariant.Backport));
        Assert.Equal("Lighthouse - Vanilla", VariantDisplay.For(map, "anything else"));
    }

    // The composed label uses the DECLARED name, not the key.
    [Fact]
    public void AComposedLabelUsesTheDeclaredNameNotTheKey()
    {
        var map = Set().For("factory4_night")!;
        Assert.Equal("Factory - Classic", VariantDisplay.For(map, MapVariant.Backport));
        Assert.Equal("Factory - Vanilla", VariantDisplay.For(map, MapVariant.Original));
        Assert.Equal("Classic", VariantDisplay.NameFor(map, MapVariant.Backport));
    }

    [Fact]
    public void ADuplicateIdIsRefused()
    {
        var set = Set();
        Assert.Throws<System.ArgumentException>(() =>
            set.Add(new ManagedMap(new[] { "Lighthouse" }, "x", "a", "b")));
    }
}
