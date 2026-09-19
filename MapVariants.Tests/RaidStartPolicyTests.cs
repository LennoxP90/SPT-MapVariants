using MapVariants.Shared;
using Xunit;

namespace MapVariants.Tests;

public class RaidStartPolicyTests
{
    [Fact]
    public void AClientThatChoseNothingAsksTheServer()
    {
        Assert.True(RaidStartPolicy.NeedsServerAnswer("lighthouse", null, null));
        Assert.True(RaidStartPolicy.NeedsServerAnswer("lighthouse", "lighthouse", null));
        Assert.True(RaidStartPolicy.NeedsServerAnswer("lighthouse", "lighthouse", ""));
    }

    [Fact]
    public void AChoiceForThisLocationEndsIt()
    {
        Assert.False(RaidStartPolicy.NeedsServerAnswer("lighthouse", "lighthouse", MapVariant.Original));
        Assert.False(RaidStartPolicy.NeedsServerAnswer("lighthouse", "lighthouse", MapVariant.Backport));
    }

    [Fact]
    public void ACaseOrSpacingDifferenceIsStillTheSameLocation()
    {
        Assert.False(RaidStartPolicy.NeedsServerAnswer("Lighthouse", " lighthouse ", MapVariant.Original));
        Assert.False(RaidStartPolicy.NeedsServerAnswer(" LIGHTHOUSE", "Lighthouse", MapVariant.Backport));
    }

    [Fact]
    public void AChoiceForAnotherLocationIsNotAnAnswerForThisOne()
    {
        // The transit case: the source raid's choice is still recorded when the destination's raid
        // starts, and it says nothing about the destination.
        Assert.True(RaidStartPolicy.NeedsServerAnswer("interchange", "lighthouse", MapVariant.Original));
    }

    [Fact]
    public void ARaidWithNoLocationAsksNothing()
    {
        Assert.False(RaidStartPolicy.NeedsServerAnswer(null, null, null));
        Assert.False(RaidStartPolicy.NeedsServerAnswer("", "lighthouse", MapVariant.Backport));
        Assert.False(RaidStartPolicy.NeedsServerAnswer("   ", null, null));
    }
}
