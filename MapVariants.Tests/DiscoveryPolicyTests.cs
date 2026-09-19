using MapVariants.Shared;
using Xunit;

namespace MapVariants.Tests;

public class DiscoveryPolicyTests
{
    [Fact]
    public void UnchangedReferencesMeanNoBackport()
    {
        var a = new object(); var b = new object();
        Assert.False(DiscoveryPolicy.HasBackport(new object?[] { a, b, null }, new object?[] { a, b, null }));
    }

    [Fact]
    public void AnyReplacedReferenceMeansABackport()
    {
        var a = new object(); var b = new object();
        var now = new object?[] { a, new object(), null };
        Assert.True(DiscoveryPolicy.HasBackport(new object?[] { a, b, null }, now));
        Assert.Equal(new[] { 1 }, DiscoveryPolicy.ChangedIndices(new object?[] { a, b, null }, now));
    }

    // Equal by value is not equal by reference: a re-deserialised copy still counts as replaced.
    [Fact]
    public void ComparisonIsByReference()
    {
        Assert.True(DiscoveryPolicy.HasBackport(new object?[] { "x" }, new object?[] { new string('x', 1) }));
    }

    // It cannot happen from the real caller today, but the failure mode if it ever does is silent.
    [Fact]
    public void ATrailingAdditionIsAChange()
    {
        var a = new object();
        Assert.True(DiscoveryPolicy.HasBackport(new object?[] { a }, new object?[] { a, new object() }));
        Assert.Equal(new[] { 1 }, DiscoveryPolicy.ChangedIndices(new object?[] { a }, new object?[] { a, new object() }));
    }

    [Fact]
    public void ATrailingRemovalIsAChange()
    {
        var a = new object();
        var b = new object();
        Assert.True(DiscoveryPolicy.HasBackport(new object?[] { a, b }, new object?[] { a }));
        Assert.Equal(new[] { 1 }, DiscoveryPolicy.ChangedIndices(new object?[] { a, b }, new object?[] { a }));
    }
}
