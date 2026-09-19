using MapVariants.Client;
using MapVariants.Shared;
using Xunit;

namespace MapVariants.Tests;

public class MapsFileTests
{
    const string Sample = """
    {
      "maps": [
        { "locationIds": ["lighthouse"], "pluginGuid": "com.manimal.lighthouse",
          "displayName": "Lighthouse", "backportName": "Manimal", "originalName": "Vanilla" },
        { "locationIds": ["Interchange"], "pluginGuid": "com.manimal.interchange",
          "displayName": "Interchange", "backportName": "Manimal", "originalName": "Vanilla" }
      ]
    }
    """;

    [Fact]
    public void ParsesEveryEntry()
    {
        var maps = MapsFile.Parse(Sample);
        Assert.Equal(2, maps.Length);
        Assert.Equal("lighthouse", maps[0].Key);
        Assert.Equal("com.manimal.lighthouse", maps[0].PluginGuid);
        Assert.Equal("interchange", maps[1].Key);
        Assert.Equal("Manimal", maps[1].BackportName);
    }

    [Fact]
    public void TilePathsAreDerivedFromTheKey()
    {
        var map = MapsFile.Parse(Sample)[0];
        Assert.Equal("tiles/lighthouse-backport.png", map.BackportTile);
        Assert.Equal("tiles/lighthouse-original.png", map.OriginalTile);
    }

    // Camel case is the file's contract, and the variant names fall back to the two bare words.
    [Fact]
    public void MissingVariantNamesFallBackToTheBareWords()
    {
        var maps = MapsFile.Parse("""{ "maps": [ { "locationIds": ["woods"], "pluginGuid": "x" } ] }""");
        Assert.Equal("Retail", maps[0].BackportName);
        Assert.Equal("Vanilla", maps[0].OriginalName);
        Assert.Equal("Woods - Retail", VariantDisplay.For(maps[0], MapVariant.Backport));
    }

    [Fact]
    public void AnEntryWithNoIdsIsSkippedNotFatal()
    {
        var maps = MapsFile.Parse("""{ "maps": [ { "pluginGuid": "x" }, { "locationIds": ["woods"], "pluginGuid": "y" } ] }""");
        Assert.Single(maps);
    }

    // The entry used to be dropped because the FIRST id was blank, even with a valid one after it.
    [Fact]
    public void ABlankFirstIdDoesNotDiscardTheEntry()
    {
        var maps = MapsFile.Parse("""{ "maps": [ { "locationIds": ["", "factory4_night"], "pluginGuid": "x" } ] }""");
        Assert.Single(maps);
        Assert.Equal("factory4_night", maps[0].Key);
        Assert.Equal(new[] { "factory4_night" }, maps[0].LocationIds);
        Assert.Equal("tiles/factory4_night-backport.png", maps[0].BackportTile);
        Assert.Equal("tiles/factory4_night-original.png", maps[0].OriginalTile);
    }

    [Fact]
    public void AnEntryWhoseIdsAreAllBlankIsSkippedNotFatal()
    {
        var maps = MapsFile.Parse("""{ "maps": [ { "locationIds": ["", "  "], "pluginGuid": "x" }, { "locationIds": ["woods"] } ] }""");
        Assert.Single(maps);
        Assert.Equal("woods", maps[0].Key);
    }

    // It has to survive WithTiles, or the value is parsed and then dropped on the floor.
    [Fact]
    public void AStandDownTargetIsCarriedThrough()
    {
        var maps = MapsFile.Parse("""
        { "maps": [ { "locationIds": ["lighthouse"], "pluginGuid": "x",
                      "standDown": "Manimal.Lighthouse.Client.LighthouseSceneLoader:ReleaseIfUnused" } ] }
        """);
        Assert.Equal("Manimal.Lighthouse.Client.LighthouseSceneLoader:ReleaseIfUnused", maps[0].StandDown);
        Assert.Equal("tiles/lighthouse-backport.png", maps[0].BackportTile);
    }

    [Fact]
    public void AnEntryWithNoStandDownHasNone()
    {
        var map = MapsFile.Parse("""{ "maps": [ { "locationIds": ["interchange"], "pluginGuid": "x" } ] }""")[0];
        Assert.Null(map.StandDown);
        Assert.Equal(StandDownDeclaration.Undeclared, map.Declaration);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("NONE")]
    [InlineData("None")]
    [InlineData("  none  ")]
    public void TheReservedWordNoneIsRecognisedCaseInsensitively(string standDown)
    {
        var map = MapsFile.Parse($$"""{ "maps": [ { "locationIds": ["interchange"], "pluginGuid": "x", "standDown": "{{standDown}}" } ] }""")[0];
        Assert.Equal(StandDownDeclaration.Safe, map.Declaration);
    }

    [Fact]
    public void ARealMethodReferenceIsTheTargetState()
    {
        var map = MapsFile.Parse("""
        { "maps": [ { "locationIds": ["lighthouse"], "pluginGuid": "x",
                      "standDown": "Manimal.Lighthouse.Client.LighthouseSceneLoader:ReleaseIfUnused" } ] }
        """)[0];
        Assert.Equal(StandDownDeclaration.Target, map.Declaration);
        Assert.Equal("Manimal.Lighthouse.Client.LighthouseSceneLoader:ReleaseIfUnused", map.StandDown);
    }

    [Fact]
    public void AnAbsentStandDownIsUndeclared()
    {
        var map = MapsFile.Parse("""{ "maps": [ { "locationIds": ["woods"], "pluginGuid": "x" } ] }""")[0];
        Assert.Null(map.StandDown);
        Assert.Equal(StandDownDeclaration.Undeclared, map.Declaration);
    }

    // A typo must degrade to Undeclared, the refusing state, and never to Safe.
    [Theory]
    [InlineData("NoColonHere")]
    [InlineData("Type:")]
    [InlineData(":Method")]
    [InlineData("non")]
    [InlineData("noone")]
    public void AMalformedValueDegradesToUndeclaredNotToSafe(string standDown)
    {
        var map = MapsFile.Parse($$"""{ "maps": [ { "locationIds": ["woods"], "pluginGuid": "x", "standDown": "{{standDown}}" } ] }""")[0];
        Assert.Null(map.StandDown);
        Assert.Equal(StandDownDeclaration.Undeclared, map.Declaration);
    }

    // A typo costs the refusal path at worst; the entry itself still parses.
    [Theory]
    [InlineData("NoColonHere")]
    [InlineData("Type:")]
    [InlineData(":Method")]
    [InlineData("Type:Method:Extra")]
    [InlineData("   ")]
    [InlineData("Some Type:Method")]
    public void AMalformedStandDownIsIgnoredNotFatal(string standDown)
    {
        var maps = MapsFile.Parse($$"""{ "maps": [ { "locationIds": ["woods"], "pluginGuid": "x", "standDown": "{{standDown}}" } ] }""");
        Assert.Single(maps);
        Assert.Equal("woods", maps[0].Key);
        Assert.Null(maps[0].StandDown);
    }

    [Fact]
    public void EmptyOrMissingIsAnEmptySet()
    {
        Assert.Empty(MapsFile.Parse(""));
        Assert.Empty(MapsFile.Parse("{}"));
    }

    // Absent means the capitalised key, because "lighthouse" reads wrong in an F12 heading.
    [Fact]
    public void DisplayNameDefaultsToTheCapitalisedKeyWhenAbsent()
    {
        var maps = MapsFile.Parse("""{ "maps": [ { "locationIds": ["lighthouse"] }, { "locationIds": ["Interchange"] } ] }""");
        Assert.Equal("Lighthouse", maps[0].DisplayName);
        Assert.Equal("Interchange", maps[1].DisplayName);
    }

    [Fact]
    public void TheMapNameIsDeclaredOnceAndComposedIn()
    {
        var maps = MapsFile.Parse(Sample);
        Assert.Equal("Lighthouse", maps[0].DisplayName);
        Assert.Equal("Manimal", maps[0].BackportName);
        Assert.Equal("Vanilla", maps[0].OriginalName);
        Assert.Equal("Lighthouse - Manimal", VariantDisplay.For(maps[0], MapVariant.Backport));
        Assert.Equal("Lighthouse - Vanilla", VariantDisplay.For(maps[0], MapVariant.Original));
    }

    [Fact]
    public void DisplayNameIsReadWhenSupplied()
    {
        var maps = MapsFile.Parse("""{ "maps": [ { "locationIds": ["factory4_day","factory4_night"], "pluginGuid": "x", "displayName": "Factory" } ] }""");
        Assert.Equal("Factory", maps[0].DisplayName);
        Assert.Equal("factory4_day", maps[0].Key);
    }

    [Fact]
    public void ADisplayNameDoesNotMoveTheTilePaths()
    {
        var maps = MapsFile.Parse("""{ "maps": [ { "locationIds": ["factory4_day"], "pluginGuid": "x", "displayName": "Factory" } ] }""");
        Assert.Equal("tiles/factory4_day-backport.png", maps[0].BackportTile);
        Assert.Equal("tiles/factory4_day-original.png", maps[0].OriginalTile);
    }
}
