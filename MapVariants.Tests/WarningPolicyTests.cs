using MapVariants.Shared;
using Xunit;

namespace MapVariants.Tests;

public class WarningPolicyTests
{
    [Fact]
    public void NoSupplierMeansNothingToShow()
    {
        Assert.Empty(WarningPolicy.Normalise(null));
        Assert.Equal("", WarningPolicy.Text(null));
        Assert.False(WarningPolicy.Shows(backport: true, null));
        Assert.Equal("NEXT", WarningPolicy.AcceptCaption(backport: true, null));
    }

    // Without this the window shows a blank line over a CONFIRM button.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankLineIsNotAWarning(string blank)
    {
        var warnings = new[] { blank };
        Assert.Empty(WarningPolicy.Normalise(warnings));
        Assert.False(WarningPolicy.Shows(backport: true, warnings));
        Assert.Equal("NEXT", WarningPolicy.AcceptCaption(backport: true, warnings));
    }

    [Fact]
    public void BlanksAreDroppedFromAroundRealLines()
    {
        var text = WarningPolicy.Text(new[] { "  One.  ", "", null!, "Two." });
        Assert.Equal("One.  Two.", text);
    }

    [Fact]
    public void SeveralSentencesAreJoinedNotComposed()
    {
        Assert.Equal("One.  Two.  Three.", WarningPolicy.Text(new[] { "One.", "Two.", "Three." }));
    }

    [Fact]
    public void OneLineIsPassedThroughUnchanged()
    {
        Assert.Equal("Six accepted quests cannot be finished here.",
                     WarningPolicy.Text(new[] { "Six accepted quests cannot be finished here." }));
    }

    [Fact]
    public void TheShippedTileNeverCarriesTheWarning()
    {
        var warnings = new[] { "Something to know." };
        Assert.False(WarningPolicy.Shows(backport: false, warnings));
        Assert.Equal("NEXT", WarningPolicy.AcceptCaption(backport: false, warnings));
        Assert.True(WarningPolicy.Shows(backport: true, warnings));
        Assert.Equal("CONFIRM", WarningPolicy.AcceptCaption(backport: true, warnings));
    }
}

public class ManagedMapDisplayNameTests
{
    static ManagedMap Map(string? displayName)
        => new ManagedMap(new[] { "factory4_day", "factory4_night" }, null, "b", "o", displayName: displayName);

    [Fact]
    public void DisplayNameDefaultsToTheCapitalisedKey()
    {
        Assert.Equal("Factory4_day", Map(null).DisplayName);
        Assert.Equal("Factory4_day", Map("").DisplayName);
        Assert.Equal("Factory4_day", Map("   ").DisplayName);
    }

    [Fact]
    public void ASuppliedDisplayNameIsTrimmedAndKept()
    {
        Assert.Equal("Factory", Map("  Factory  ").DisplayName);
        Assert.Equal("factory4_day", Map("Factory").Key);
    }

    // A DisplayName lost in WithTiles would rename an F12 section.
    [Fact]
    public void WithTilesCarriesTheDisplayName()
    {
        var map = Map("Factory").WithTiles("tiles/b.png", "tiles/o.png");
        Assert.Equal("Factory", map.DisplayName);
        Assert.Equal("tiles/b.png", map.BackportTile);
    }

    [Fact]
    public void ADefaultedDisplayNameSurvivesWithTilesAsTheKey()
    {
        Assert.Equal("Factory4_day", Map(null).WithTiles("a", "b").DisplayName);
    }

    // A fallback that spelled the key out would compose to "Factory - factory4_day - Retail".
    [Fact]
    public void VariantNamesFallBackToTheBareWords()
    {
        var map = new ManagedMap(new[] { "factory4_day" }, null, "", "   ", displayName: "Factory");
        Assert.Equal("Retail", map.BackportName);
        Assert.Equal("Vanilla", map.OriginalName);
        Assert.Equal("Factory - Retail", VariantDisplay.For(map, MapVariant.Backport));
    }

    [Fact]
    public void VariantNamesAreTrimmedAndKept()
    {
        var map = new ManagedMap(new[] { "factory4_day" }, null, "  Classic  ", " Vanilla ");
        Assert.Equal("Classic", map.BackportName);
        Assert.Equal("Vanilla", map.OriginalName);
    }

    // A variant name lost in WithTiles would relabel the prompt tiles.
    [Fact]
    public void WithTilesCarriesTheVariantNames()
    {
        var map = new ManagedMap(new[] { "factory4_day" }, null, "Classic", "Vanilla", displayName: "Factory")
            .WithTiles("tiles/b.png", "tiles/o.png");
        Assert.Equal("Classic", map.BackportName);
        Assert.Equal("Vanilla", map.OriginalName);
        Assert.Equal("Factory", map.DisplayName);
    }
}

// The map name is composed in, never stored, so the two can never drift.
public class VariantDisplayComposeTests
{
    [Fact]
    public void BothHalvesAreJoinedWithASpacedHyphen()
    {
        Assert.Equal("Lighthouse - Manimal", VariantDisplay.Compose("Lighthouse", "Manimal"));
        Assert.Equal("Lighthouse - Vanilla", VariantDisplay.Compose("  Lighthouse  ", "  Vanilla  "));
    }

    // Otherwise the player reads " - Manimal" on a tile.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankDisplayNameDropsTheSeparator(string? blank)
        => Assert.Equal("Manimal", VariantDisplay.Compose(blank, "Manimal"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankVariantNameLeavesTheMapName(string? blank)
        => Assert.Equal("Lighthouse", VariantDisplay.Compose("Lighthouse", blank));

    [Fact]
    public void BothBlankIsEmpty()
        => Assert.Equal("", VariantDisplay.Compose(null, null));
}

// The F12 DefaultSelection stores one of the map's own two NAMES, never a fixed word.
public class DefaultSelectionTests
{
    [Fact]
    public void EachDeclaredNameMapsToItsOwnVariant()
    {
        Assert.Equal(MapVariant.Backport, DefaultSelection.ToVariant("Manimal", "Manimal", "Vanilla"));
        Assert.Equal(MapVariant.Original, DefaultSelection.ToVariant("Vanilla", "Manimal", "Vanilla"));
        Assert.Equal(MapVariant.Backport, DefaultSelection.ToVariant("Classic", "Classic", "Vanilla"));
    }

    [Theory]
    [InlineData("  vanilla  ")]
    [InlineData("VANILLA")]
    public void TheMatchIsTrimmedAndCaseInsensitive(string stored)
        => Assert.Equal(MapVariant.Original, DefaultSelection.ToVariant(stored, "Manimal", "Vanilla"));

    // Anything unrecognised falls back to the backport, this mod's documented default.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Retail")]
    [InlineData("whatever the last version wrote")]
    public void AnUnrecognisedValueFallsBackToTheBackport(string? stored)
        => Assert.Equal(MapVariant.Backport, DefaultSelection.ToVariant(stored, "Manimal", "Vanilla"));

    // Two blanks must not compare equal, or the map resolves to whichever side was tested first.
    [Fact]
    public void ABlankDeclaredNameMatchesNothing()
    {
        Assert.Equal(MapVariant.Backport, DefaultSelection.ToVariant("", "", ""));
        Assert.Equal(MapVariant.Backport, DefaultSelection.ToVariant("Vanilla", "Manimal", ""));
    }

    [Fact]
    public void ADuplicatedNameResolvesToTheBackport()
        => Assert.Equal(MapVariant.Backport, DefaultSelection.ToVariant("Same", "Same", "Same"));

    [Fact]
    public void TheMapOverloadReadsTheMapsOwnNames()
    {
        var map = new ManagedMap(new[] { "lighthouse" }, null, "Manimal", "Vanilla", displayName: "Lighthouse");
        Assert.Equal(MapVariant.Original, DefaultSelection.ToVariant("Vanilla", map));
        Assert.Equal(MapVariant.Backport, DefaultSelection.ToVariant("Manimal", map));
    }
}
