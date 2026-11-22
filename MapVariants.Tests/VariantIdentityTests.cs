using MapVariants.Shared;
using Xunit;

namespace MapVariants.Tests;

public class VariantIdentityTests
{
    static ManagedMap Discovered() => new(new[] { "lighthouse" }, "com.manimal.lighthouse", "Manimal", "Vanilla", displayName: "Lighthouse");

    static ManagedMap Provider() => new(new[] { "factory4_day", "factory4_night" }, null, "Classic", "Vanilla", displayName: "Factory",
                                        extras: new[] { new ExtraVariant("test-halloween", "Halloween 2026") });

    static string? NoSource(string id) => null;

    [Theory]
    [InlineData("Halloween 2026", "halloween-2026")]
    [InlineData("  Manimal ", "manimal")]
    [InlineData("Stub B!!", "stub-b")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void SlugKeepsLettersAndDigits(string? name, string expected) => Assert.Equal(expected, VariantIdentity.Slug(name));

    [Fact]
    public void TheShippedMapIsSpt()
        => Assert.Equal(("spt", "shipped"), VariantIdentity.For(Discovered(), "original", null, NoSource));

    [Fact]
    public void ADiscoveredBackportIsItsPluginGuidAndName()
        => Assert.Equal(("com.manimal.lighthouse", "manimal"), VariantIdentity.For(Discovered(), "backport", null, NoSource));

    [Fact]
    public void AProviderIsWhoeverRegisteredIt()
    {
        Assert.Equal(("com.lennoxp90.factoryclassic", "classic"),
                     VariantIdentity.For(Provider(), "backport", "com.lennoxp90.factoryclassic", NoSource));
        Assert.Equal(("com.lennoxp90.factoryclassic", "test-halloween"),
                     VariantIdentity.For(Provider(), "test-halloween", "com.lennoxp90.factoryclassic", NoSource));
    }

    [Fact]
    public void AProviderNobodyCouldNameIsUnknown()
        => Assert.Equal(("unknown", "classic"), VariantIdentity.For(Provider(), "backport", null, NoSource));

    [Fact]
    public void ASourceIsItsOwnPluginAndId()
    {
        var map = Discovered().WithSource(new VariantSource("test-stub", "Stub"));
        Assert.Equal(("com.example.stub", "test-stub"),
                     VariantIdentity.For(map, "test-stub", null, id => id == "test-stub" ? "com.example.stub" : null));
        Assert.Equal(("com.manimal.lighthouse", "manimal"), VariantIdentity.For(map, "backport", null, NoSource));
    }

    [Fact]
    public void ComposeJoinsWithAColon() => Assert.Equal("spt:shipped", VariantIdentity.Compose("spt", "shipped"));
}
