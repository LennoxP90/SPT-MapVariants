using MapVariants.Shared;
using Xunit;

namespace MapVariants.Tests;

public class SuspendPolicyTests
{
    static readonly RecordedPatch[] All =
    {
        new RecordedPatch("Prefix", "LoadPresetAsyncPatch", "ManimalLighthouse", "Prefix", 400),
        new RecordedPatch("Prefix", "LoadPresetPatch", "ManimalInterchange", "Prefix", 400),
        new RecordedPatch("Prefix", "com.lennoxp90.factoryclassic.presetswap", "FactoryClassic.Client", "BeforePresetLoad", 400),
        new RecordedPatch("Prefix", "com.lennoxp90.mapvariants", "MapVariants.Client", "Before", 800),
    };

    const string Ours = "MapVariants.Client";

    // ModulePatch registers under the CLASS name, so the assembly is the only reliable identity.
    [Fact]
    public void SelectsByAssemblyNotOwner()
    {
        var picked = SuspendPolicy.Select(All, "ManimalLighthouse", Ours);
        Assert.Single(picked);
        Assert.Equal("LoadPresetAsyncPatch", picked[0].Owner);
    }

    // A bad maps.json entry cannot make us unpatch ourselves from inside our own prefix.
    [Fact]
    public void NeverSelectsOurOwnPatchEvenWhenAskedFor()
        => Assert.Empty(SuspendPolicy.Select(All, Ours, Ours));

    [Fact]
    public void SelectsNothingForAnAssemblyWithNoPatches()
        => Assert.Empty(SuspendPolicy.Select(All, "Nothing", Ours));

    // Suspend and Restore are always attempted now, and the record says what actually moved.
    [Theory]
    [InlineData(0, 0, false)]   // nothing selected, nothing suspended: no change
    [InlineData(0, 1, true)]    // first stock raid: one patch suspended
    [InlineData(1, 1, false)]   // second consecutive stock raid: already suspended, nothing moved
    [InlineData(1, 0, true)]    // backport raid after a stock one: restored
    [InlineData(2, 1, true)]    // partial restore: still a change
    public void ThePatchSetChangedWhenTheRecordMoved(int before, int after, bool expected)
        => Assert.Equal(expected, SuspendPolicy.PatchSetChanged(before, after));

    // Counting the SELECTION made a second consecutive shipped raid look like a backport with no
    // patch, because the first raid had already removed it.
    [Theory]
    [InlineData(true, 0, false)]   // stock, nothing of the backport's left live: correct raid
    [InlineData(true, 1, true)]    // stock, one patch still live: withdraw, it would load its scenes
    [InlineData(true, 3, true)]    // stock, a failed suspension: withdraw
    [InlineData(false, 0, false)]  // backport raid: never withdraws
    [InlineData(false, 2, false)]  // backport raid with its patches live: that is the point
    public void WithdrawsOnlyWhenAStockRaidLeftOneOfTheBackportsPatchesLive(bool wantStock, int stillLive, bool expected)
        => Assert.Equal(expected, SuspendPolicy.MustWithdrawVeto(wantStock, stillLive));

    // One live patch is enough: the backport's prefix takes the whole load over.
    [Theory]
    [InlineData(0, MapVariant.Original)]
    [InlineData(1, MapVariant.Backport)]
    [InlineData(4, MapVariant.Backport)]
    public void TheLivePatchesDecideWhatLoads(int stillLive, string expected)
        => Assert.Equal(expected, SuspendPolicy.WillLoad(stillLive));

    // The load path cannot change the patch set safely, so what is already true wins.
    [Theory]
    [InlineData(MapVariant.Original, MapVariant.Original, false)]   // chose the shipped map and it is what loads
    [InlineData(MapVariant.Backport, MapVariant.Backport, false)]   // chose the backport and it is what loads
    [InlineData(MapVariant.Original, MapVariant.Backport, true)]    // the suspension never took: adopt the backport
    [InlineData(MapVariant.Backport, MapVariant.Original, true)]    // the patches are still suspended: adopt the shipped map
    [InlineData("ORIGINAL", MapVariant.Original, false)]            // the choice is normalised, not compared raw
    public void AdoptsThePatchStateOnlyWhenItDiffersFromTheChoice(string choice, string willLoad, bool expected)
        => Assert.Equal(expected, SuspendPolicy.MustAdoptPatchState(choice, willLoad));

    // Nobody answered is not "the shipped map": the server's table rests on the backport.
    [Theory]
    [InlineData(null, MapVariant.Backport, false)]
    [InlineData(null, MapVariant.Original, true)]
    [InlineData("", MapVariant.Backport, false)]
    [InlineData("", MapVariant.Original, true)]
    public void AnUnansweredSessionCountsAsTheBackport(string? choice, string willLoad, bool expected)
        => Assert.Equal(expected, SuspendPolicy.MustAdoptPatchState(choice, willLoad));
}
