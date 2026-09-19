using MapVariants.Shared;
using Xunit;

namespace MapVariants.Tests;

public class StandDownPolicyTests
{
    // Every combination of the three inputs, not a sample: the first two each gate and short-circuit
    // to Proceed, and only when both are true does the declaration decide. docs/DESIGN-NOTES.md has
    // why Lighthouse needs a stand-down and Interchange does not.
    [Theory]
    [InlineData(false, false, StandDownDeclaration.Undeclared, StandDownAction.Proceed)]
    [InlineData(false, false, StandDownDeclaration.Safe, StandDownAction.Proceed)]
    [InlineData(false, false, StandDownDeclaration.Target, StandDownAction.Proceed)]
    [InlineData(false, true, StandDownDeclaration.Undeclared, StandDownAction.Proceed)]
    [InlineData(false, true, StandDownDeclaration.Safe, StandDownAction.Proceed)]
    [InlineData(false, true, StandDownDeclaration.Target, StandDownAction.Proceed)]
    [InlineData(true, false, StandDownDeclaration.Undeclared, StandDownAction.Proceed)]
    [InlineData(true, false, StandDownDeclaration.Safe, StandDownAction.Proceed)]
    [InlineData(true, false, StandDownDeclaration.Target, StandDownAction.Proceed)]
    [InlineData(true, true, StandDownDeclaration.Undeclared, StandDownAction.Refuse)]
    [InlineData(true, true, StandDownDeclaration.Safe, StandDownAction.Proceed)]
    [InlineData(true, true, StandDownDeclaration.Target, StandDownAction.StandDown)]
    public void EveryCombinationOfInputsDecidesAsExpected(bool wantStock, bool loaded, StandDownDeclaration declaration, StandDownAction expected)
        => Assert.Equal(expected, StandDownPolicy.Decide(wantStock, loaded, declaration));

    // An attempt that threw leaves exactly the state the decision was trying to clear.
    [Theory]
    [InlineData(true, StandDownAction.Proceed)]
    [InlineData(false, StandDownAction.Refuse)]
    public void AFailedStandDownRefuses(bool succeeded, StandDownAction expected)
        => Assert.Equal(expected, StandDownPolicy.AfterAttempt(succeeded));

    [Fact]
    public void ParsesTypeAndMethod()
    {
        var target = StandDownTarget.Parse("Manimal.Lighthouse.Client.LighthouseSceneLoader:ReleaseIfUnused");
        Assert.NotNull(target);
        Assert.Equal("Manimal.Lighthouse.Client.LighthouseSceneLoader", target!.TypeName);
        Assert.Equal("ReleaseIfUnused", target.MethodName);
    }

    [Fact]
    public void SurroundingWhitespaceIsTrimmed()
    {
        var target = StandDownTarget.Parse("  Some.Type : Method  ");
        Assert.NotNull(target);
        Assert.Equal("Some.Type", target!.TypeName);
        Assert.Equal("Method", target.MethodName);
    }

    // Anything that is not one type and one method yields null, and nothing throws on a typo.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NoColonHere")]
    [InlineData("Type:")]
    [InlineData(":Method")]
    [InlineData("Type:Method:Extra")]
    [InlineData("Some Type:Method")]
    [InlineData("Type:Some Method")]
    public void AMalformedSpecIsNull(string? spec)
        => Assert.Null(StandDownTarget.Parse(spec));

    [Fact]
    public void RoundTripsToTheSpecItWasParsedFrom()
        => Assert.Equal("A.B:C", StandDownTarget.Parse("A.B:C")!.ToString());
}
