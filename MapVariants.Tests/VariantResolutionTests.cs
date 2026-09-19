using MapVariants.Shared;
using Xunit;

namespace MapVariants.Tests;

public class VariantResolutionTests
{
    const string B = MapVariant.Backport;
    const string O = MapVariant.Original;

    [Fact]
    public void AnOwnChoiceAlwaysWins()
    {
        Assert.Equal(O, VariantResolution.Resolve(O, B));
        Assert.Equal(B, VariantResolution.Resolve(B, O));
    }

    // A Fika headless hosts under its own session and never has a choice of its own.
    [Fact]
    public void WithNoChoiceOfItsOwnTheLastChoiceIsInherited()
    {
        Assert.Equal(O, VariantResolution.Resolve(null, O));
        Assert.Equal(O, VariantResolution.Resolve(VariantResolution.NoAnswer, O));
    }

    // The resting state is the backport: there is no server default to configure.
    [Fact]
    public void WithNothingRecordedTheBackportApplies()
    {
        Assert.Equal(B, VariantResolution.Resolve(null, null));
        Assert.Equal(B, VariantResolution.Resolve(VariantResolution.NoAnswer, VariantResolution.NoAnswer));
    }

    [Fact]
    public void EveryAnswerIsNormalised()
    {
        Assert.Equal(B, VariantResolution.Resolve("BACKPORT", null));
        Assert.Equal(O, VariantResolution.Resolve(null, "vanilla"));
    }

    [Fact]
    public void HasAnswerIsFalseWhenAllThreeAreEmpty()
    {
        Assert.False(VariantResolution.HasAnswer(null, null, null));
        Assert.False(VariantResolution.HasAnswer(VariantResolution.NoAnswer, VariantResolution.NoAnswer, VariantResolution.NoAnswer));
    }

    [Fact]
    public void HasAnswerIsTrueWhenOnlyTheClaimIsSet()
    {
        Assert.True(VariantResolution.HasAnswer(B, null, null));
    }

    [Fact]
    public void HasAnswerIsTrueWhenOnlyTheOwnChoiceIsSet()
    {
        Assert.True(VariantResolution.HasAnswer(null, O, null));
    }

    [Fact]
    public void HasAnswerIsTrueWhenOnlyTheLatestForLocationIsSet()
    {
        Assert.True(VariantResolution.HasAnswer(null, null, B));
    }

    [Fact]
    public void HasAnswerTreatsNoAnswerAsEmptyInEveryPosition()
    {
        Assert.False(VariantResolution.HasAnswer(VariantResolution.NoAnswer, null, null));
        Assert.False(VariantResolution.HasAnswer(null, VariantResolution.NoAnswer, null));
        Assert.False(VariantResolution.HasAnswer(null, null, VariantResolution.NoAnswer));
    }
}
