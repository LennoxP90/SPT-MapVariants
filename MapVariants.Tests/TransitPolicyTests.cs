using MapVariants.Shared;
using Xunit;

namespace MapVariants.Tests;

public class TransitPolicyTests
{
    [Fact]
    public void AnUnclaimedDestinationTakesWhatIsOffered()
    {
        Assert.True(TransitPolicy.WasFirst(null));
        Assert.True(TransitPolicy.WasFirst(""));
        Assert.Equal(MapVariant.Original, TransitPolicy.Winner(null, MapVariant.Original));
    }

    [Fact]
    public void AClaimedDestinationIgnoresWhatIsOfferedAfterwards()
    {
        Assert.False(TransitPolicy.WasFirst(MapVariant.Original));
        Assert.Equal(MapVariant.Original, TransitPolicy.Winner(MapVariant.Original, MapVariant.Backport));
        Assert.Equal(MapVariant.Backport, TransitPolicy.Winner(MapVariant.Backport, MapVariant.Original));
    }

    [Fact]
    public void AnythingUnrecognisedIsTheShippedMap()
        => Assert.Equal(MapVariant.Original, TransitPolicy.Winner(null, "nonsense"));
}
