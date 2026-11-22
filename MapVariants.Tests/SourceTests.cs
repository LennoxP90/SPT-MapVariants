using MapVariants.Shared;
using Xunit;

namespace MapVariants.Tests;

// Several mods each owning a variant of one map (API 3.2): a maps.json backport plus a source, or
// sources alone. The ids carry a test- prefix because MapVariant's declared set is process-wide.
public class SourceTests
{
    static ManagedMap Backported() => new(new[] { "bigmap" }, "com.example.backport", "Fums", "Vanilla",
                                          "fums.png", "vanilla.png", "Example.Loader:Release", "Customs");

    static VariantSource Classic() => new(" Test-Classic ", "Classic", "classic.png");

    static ManagedMap SourceOnly() => ManagedMap.ForSource(new[] { "bigmap" }, "Customs", "Vanilla", "vanilla.png", Classic());

    [Fact]
    public void ASourceJoinsTheBackportAfterItsVariants()
    {
        var map = Backported().WithSource(Classic());
        Assert.Equal(new[] { "original", "backport", "test-classic" }, map.Variants);
        Assert.True(map.HasBackport);
        Assert.Equal("backport", map.Resting);
        Assert.Equal("com.example.backport", map.PluginGuid);
    }

    [Fact]
    public void NamesTilesAndOwnersResolvePerVariant()
    {
        var map = Backported().WithSource(Classic());
        Assert.Equal("Customs - Classic", VariantDisplay.For(map, "test-classic"));
        Assert.Equal("Customs - Fums", VariantDisplay.For(map, "backport"));
        Assert.Equal("Customs - Vanilla", VariantDisplay.For(map, "original"));
        Assert.Equal("classic.png", map.TileFor("test-classic"));
        Assert.Equal("fums.png", map.TileFor("backport"));
        Assert.Equal("test-classic", map.OwnerOf("TEST-CLASSIC"));
        Assert.Equal("backport", map.OwnerOf("backport"));
        Assert.Equal("original", map.OwnerOf("original"));
    }

    [Fact]
    public void AMapOnlySourcesOfferHasNoBackportAndRestsOnTheSource()
    {
        var map = SourceOnly();
        Assert.False(map.HasBackport);
        Assert.Null(map.PluginGuid);
        Assert.Equal(new[] { "original", "test-classic" }, map.Variants);
        Assert.Equal("test-classic", map.Resting);
        // "backport" offered on a map with none means its resting source.
        Assert.Equal("test-classic", map.OwnerOf("backport"));
        Assert.Equal("test-classic", map.Offered("backport"));
        Assert.Equal("Classic", map.NameFor("backport"));
    }

    [Fact]
    public void TheRestingSourceIsTheSameWhicheverOrderTheyArrive()
    {
        var a = new VariantSource("test-zeta", "Zeta");
        var b = new VariantSource("test-alpha", "Alpha");
        var one = ManagedMap.ForSource(new[] { "bigmap" }, null, null, null, a).WithSource(b);
        var two = ManagedMap.ForSource(new[] { "bigmap" }, null, null, null, b).WithSource(a);
        Assert.Equal("test-alpha", one.Resting);
        Assert.Equal(one.Resting, two.Resting);
    }

    [Theory]
    [InlineData("original")]
    [InlineData("backport")]
    [InlineData("unmanaged")]
    [InlineData("")]
    [InlineData("test-classic")]
    [InlineData("test-halloween-src")]
    public void ASourceCannotTakeAnIdAlreadyOfferedOrReserved(string id)
    {
        var map = new ManagedMap(new[] { "bigmap" }, null, "B", "O",
                                 extras: new[] { new ExtraVariant("test-halloween-src", "Halloween") })
            .WithSource(Classic());
        Assert.False(map.CanAdd(new VariantSource(id, "x")));
        Assert.Throws<System.ArgumentException>(() => map.WithSource(new VariantSource(id, "x")));
    }

    [Fact]
    public void ABackportArrivingAfterASourceKeepsTheSource()
    {
        var merged = Backported().WithSourcesOf(SourceOnly());
        Assert.Equal(new[] { "original", "backport", "test-classic" }, merged.Variants);
        Assert.Equal("backport", merged.Resting);
        Assert.Equal("Example.Loader:Release", merged.StandDown);
    }

    [Fact]
    public void WithoutBackportLeavesTheSourcesAlone()
    {
        var map = Backported().WithSource(Classic()).WithoutBackport();
        Assert.False(map.HasBackport);
        Assert.Null(map.PluginGuid);
        Assert.Null(map.StandDown);
        Assert.Equal(new[] { "original", "test-classic" }, map.Variants);
        Assert.Equal("Customs", map.DisplayName);
    }

    [Fact]
    public void WithoutBackportOnAMapWithNoSourcesIsTheSameMap()
    {
        var map = Backported();
        Assert.Same(map, map.WithoutBackport());
    }

    [Fact]
    public void WithTilesKeepsTheSources()
        => Assert.Equal(new[] { "original", "backport", "test-classic" }, Backported().WithSource(Classic()).WithTiles("a", "b").Variants);

    [Fact]
    public void AMapWithNoBackportAndNoSourceCannotBeBuilt()
        => Assert.Throws<System.ArgumentNullException>(() => ManagedMap.ForSource(new[] { "bigmap" }, null, null, null, null!));

    // ---- what each owner is told ----

    [Theory]
    [InlineData("backport", "backport", "original")]
    [InlineData("original", "original", "original")]
    [InlineData("test-classic", "original", "backport")]
    public void EachOwnerHearsItsOwnTwoWords(string chosen, string backportHears, string classicHears)
    {
        var map = Backported().WithSource(Classic());
        Assert.Equal(backportHears, SourcePolicy.ForBackport(map, chosen));
        Assert.Equal(classicHears, SourcePolicy.ForSource(map, "test-classic", chosen));
    }

    [Theory]
    [InlineData("backport", "backport")]
    [InlineData("original", "original")]
    [InlineData("test-extra-two", "test-extra-two")]
    public void AMapWithNoSourcesTellsTheBackportExactlyWhatWasChosen(string chosen, string hears)
    {
        var map = new ManagedMap(new[] { "factory4_day" }, null, "Classic", "Vanilla",
                                 extras: new[] { new ExtraVariant("test-extra-two", "Two") });
        Assert.Equal(hears, SourcePolicy.ForBackport(map, chosen));
    }

    [Fact]
    public void TheBackportIsWantedOnlyForItsOwnVariants()
    {
        var map = Backported().WithSource(Classic());
        Assert.True(SourcePolicy.BackportWanted(map, "backport"));
        Assert.False(SourcePolicy.BackportWanted(map, "original"));
        Assert.False(SourcePolicy.BackportWanted(map, "test-classic"));
        Assert.False(SourcePolicy.BackportWanted(SourceOnly(), "backport"));
    }

    // ---- patch state against the choice ----

    [Theory]
    [InlineData("test-classic", 0, null)]          // the source's raid with the backport fully suspended
    [InlineData("test-classic", 2, "backport")]    // a patch still live: the backport loads over it
    [InlineData("backport", 2, null)]
    [InlineData("backport", 0, "original")]
    [InlineData("original", 0, null)]
    [InlineData("original", 1, "backport")]
    [InlineData(null, 1, null)]                    // no answer rests on the backport
    [InlineData(null, 0, "original")]
    public void PatchStateIsAdoptedOnlyWhenItDisagrees(string? choice, int live, string? adopted)
        => Assert.Equal(adopted, SuspendPolicy.AdoptFor(Backported().WithSource(Classic()), choice, live));

    [Theory]
    [InlineData("backport", 0)]
    [InlineData("backport", 2)]
    [InlineData("original", 0)]
    [InlineData("original", 3)]
    public void AdoptForAgreesWithTheTwoVariantRuleOnAMapWithNoSources(string choice, int live)
    {
        var map = Backported();
        var old = SuspendPolicy.MustAdoptPatchState(choice, SuspendPolicy.WillLoad(live));
        Assert.Equal(old, SuspendPolicy.AdoptFor(map, choice, live) != null);
    }

    // ---- F12 and resolution ----

    [Theory]
    [InlineData("Fums", "backport")]
    [InlineData("Vanilla", "original")]
    [InlineData("classic", "test-classic")]
    [InlineData("nonsense", "original")]
    [InlineData("", "original")]
    public void DefaultSelectionReadsAllThreeNames(string stored, string expected)
        => Assert.Equal(expected, DefaultSelection.ToVariant(stored, Backported().WithSource(Classic())));

    [Theory]
    [InlineData("Classic", "test-classic")]
    [InlineData("Vanilla", "original")]
    [InlineData("Fums", "original")]
    [InlineData("", "original")]
    public void DefaultSelectionOnASourceOnlyMapFallsToVanilla(string stored, string expected)
        => Assert.Equal(expected, DefaultSelection.ToVariant(stored, SourceOnly()));

    [Fact]
    public void ResolutionFallsToTheRestingVariantGiven()
    {
        SourceOnly();
        Assert.Equal("test-classic", VariantResolution.Resolve(null, null, "test-classic"));
        Assert.Equal("backport", VariantResolution.Resolve(null, null));
        Assert.Equal("original", VariantResolution.Resolve("original", null, "test-classic"));
    }

    // ---- the set ----

    [Fact]
    public void ReplaceSwapsTheMapInPlace()
    {
        var set = new ManagedMapSet();
        var lighthouse = new ManagedMap(new[] { "lighthouse" }, "x", "a", "b");
        var customs = Backported();
        set.Add(lighthouse);
        set.Add(customs);
        var merged = customs.WithSource(Classic());
        set.Replace(customs, merged);
        Assert.Same(merged, set.For("bigmap"));
        Assert.Same(merged, set.All[1]);
        Assert.Equal(2, set.All.Count);
    }

    [Fact]
    public void ReplaceRefusesAMergeThatMovesALocation()
    {
        var set = new ManagedMapSet();
        var customs = Backported();
        set.Add(customs);
        Assert.Throws<System.ArgumentException>(() =>
            set.Replace(customs, new ManagedMap(new[] { "bigmap", "lighthouse" }, null, "a", "b")));
    }

    [Fact]
    public void ExactlyFindsAWholeMapOnly()
    {
        var set = new ManagedMapSet();
        set.Add(new ManagedMap(new[] { "factory4_day", "factory4_night" }, null, "Classic", "Vanilla"));
        Assert.NotNull(set.Exactly(new[] { "factory4_night", "FACTORY4_DAY" }));
        Assert.Null(set.Exactly(new[] { "factory4_day" }));
        Assert.True(set.AnyManaged(new[] { "factory4_day" }));
        Assert.Null(set.Exactly(new[] { "bigmap" }));
    }

    [Fact]
    public void ASecondBackportOnALocationIsStillRefused()
    {
        var set = new ManagedMapSet();
        set.Add(Backported());
        Assert.Throws<System.ArgumentException>(() => set.Add(Backported()));
    }
}
