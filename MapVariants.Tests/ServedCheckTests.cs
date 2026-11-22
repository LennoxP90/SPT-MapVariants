using MapVariants.Shared;
using Xunit;

namespace MapVariants.Tests;

// Whether the client can load what the server serves (API 3.3). The variant word alone cannot say
// which mod is behind it: a server-only install of one Customs mod beside a client with another one
// loaded one map's scenes over the other's data. Ids carry a test- prefix because MapVariant's
// declared set is process-wide.
public class ServedCheckTests
{
    const string Ours = "com.example.backport";

    static ManagedMap Backported(string? guid = Ours) => new(new[] { "bigmap" }, guid, "Fums", "Vanilla",
                                                             displayName: "Customs");

    static ManagedMap WithClassic() => Backported().WithSource(new VariantSource("test-served-classic", "Classic"));

    static ManagedMap WithExtra() => new(new[] { "bigmap" }, Ours, "Fums", "Vanilla", displayName: "Customs",
                                        extras: new[] { new ExtraVariant("test-served-halloween", "Halloween") });

    static ManagedMap SourceOnly() => ManagedMap.ForSource(new[] { "bigmap" }, "Customs", "Vanilla", null,
                                                           new VariantSource("test-served-only", "Only"));

    [Fact]
    public void TheShippedMapAlwaysMatches()
    {
        Assert.Equal(ServedVerdict.Match, ServedCheck.Judge(Backported(), "original", null));
        Assert.Equal(ServedVerdict.Match, ServedCheck.Judge(Backported(), " ORIGINAL ", "com.other"));
    }

    [Fact]
    public void ASourceThisMapOffersMatches()
    {
        Assert.Equal(ServedVerdict.Match, ServedCheck.Judge(WithClassic(), "Test-Served-Classic", null));
        Assert.Equal(ServedVerdict.Match, ServedCheck.Judge(SourceOnly(), "test-served-only", null));
    }

    [Fact]
    public void AVariantThisMapDoesNotOfferIsUnknown()
    {
        // A source whose client plugin is not installed here: Normalise would fold it to "original".
        MapVariant.Declare("test-served-elsewhere");
        Assert.Equal(ServedVerdict.UnknownVariant, ServedCheck.Judge(Backported(), "test-served-elsewhere", null));
        Assert.Equal(ServedVerdict.UnknownVariant, ServedCheck.Judge(Backported(), "test-served-never-declared", null));
        Assert.Equal(ServedVerdict.UnknownVariant, ServedCheck.Judge(SourceOnly(), "test-served-classic", null));
    }

    [Fact]
    public void TheSameBackportPluginMatchesInAnyCase()
    {
        Assert.Equal(ServedVerdict.Match, ServedCheck.Judge(Backported(), "backport", Ours));
        Assert.Equal(ServedVerdict.Match, ServedCheck.Judge(Backported(), "BACKPORT", " COM.EXAMPLE.BACKPORT "));
    }

    [Fact]
    public void AnotherModsBackportIsAMismatch()
    {
        Assert.Equal(ServedVerdict.BackportOfAnotherMod, ServedCheck.Judge(Backported(), "backport", "com.example.other"));

        // An extra is built on the backport, so it is that backport's plugin too.
        Assert.Equal(ServedVerdict.BackportOfAnotherMod, ServedCheck.Judge(WithExtra(), "test-served-halloween", "com.example.other"));
        Assert.Equal(ServedVerdict.Match, ServedCheck.Judge(WithExtra(), "test-served-halloween", Ours));
    }

    [Fact]
    public void AnUndeclaredSideIsNotJudged()
    {
        // An older server sends no plugin, and a registered provider has no maps.json GUID: carry on as before.
        Assert.Equal(ServedVerdict.Unverified, ServedCheck.Judge(Backported(), "backport", null));
        Assert.Equal(ServedVerdict.Unverified, ServedCheck.Judge(Backported(), "backport", "   "));
        Assert.Equal(ServedVerdict.Unverified, ServedCheck.Judge(Backported(guid: null), "backport", "com.example.other"));
    }

    [Fact]
    public void ABackportWithNoBackportHereIsAMismatch()
    {
        Assert.Equal(ServedVerdict.NoBackportHere, ServedCheck.Judge(SourceOnly(), "backport", null));
        Assert.Equal(ServedVerdict.NoBackportHere, ServedCheck.Judge(Backported().WithSource(new VariantSource("test-served-b", "B")).WithoutBackport(),
                                                                     "backport", Ours));
    }

    [Fact]
    public void NoAnswerOrNoMapIsNotJudged()
    {
        Assert.Equal(ServedVerdict.Unverified, ServedCheck.Judge(Backported(), null, Ours));
        Assert.Equal(ServedVerdict.Unverified, ServedCheck.Judge(Backported(), "", Ours));
        Assert.Equal(ServedVerdict.Unverified, ServedCheck.Judge(Backported(), "unmanaged", Ours));
        Assert.Equal(ServedVerdict.Unverified, ServedCheck.Judge(null, "backport", Ours));
    }

    [Fact]
    public void OnlyMismatchesAreReported()
    {
        Assert.True(ServedCheck.IsMismatch(ServedVerdict.UnknownVariant));
        Assert.True(ServedCheck.IsMismatch(ServedVerdict.NoBackportHere));
        Assert.True(ServedCheck.IsMismatch(ServedVerdict.BackportOfAnotherMod));
        Assert.False(ServedCheck.IsMismatch(ServedVerdict.Match));
        Assert.False(ServedCheck.IsMismatch(ServedVerdict.Unverified));
        Assert.Null(ServedCheck.Describe(Backported(), "bigmap", "backport", Ours, ServedVerdict.Match));
    }

    [Fact]
    public void TheMessageNamesBothPlugins()
    {
        var line = ServedCheck.Describe(Backported(), "bigmap", "backport", "com.example.other", ServedVerdict.BackportOfAnotherMod);
        Assert.Contains("com.example.other", line);
        Assert.Contains(Ours, line);

        var unknown = ServedCheck.Describe(Backported(), "bigmap", "Test-Served-Elsewhere", null, ServedVerdict.UnknownVariant);
        Assert.Contains("'test-served-elsewhere'", unknown);
    }
}
