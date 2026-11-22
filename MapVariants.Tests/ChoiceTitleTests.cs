using MapVariants.Shared;
using Xunit;

namespace MapVariants.Tests;

public class ChoiceTitleTests
{
    [Theory]
    [InlineData("The Lab", "CHOOSE YOUR LAB")]
    [InlineData("The Labyrinth", "CHOOSE YOUR LABYRINTH")]
    [InlineData("  the  Lab ", "CHOOSE YOUR LAB")]
    [InlineData("Factory", "CHOOSE YOUR FACTORY")]
    [InlineData("Theatre", "CHOOSE YOUR THEATRE")]   // only the word "The", never a name that starts with it
    [InlineData("The", "CHOOSE YOUR THE")]           // nothing after it: keep what was given
    [InlineData(null, "CHOOSE YOUR")]
    public void LeadingTheIsDropped(string? displayName, string expected)
        => Assert.Equal(expected, VariantDisplay.ChoiceTitle(displayName));
}
