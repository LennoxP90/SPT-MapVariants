using MapVariants.Shared;
using Xunit;

namespace MapVariants.Tests;

public class ChoiceLayoutTests
{
    [Fact]
    public void TwoTilesKeepTheOriginalLayout()
    {
        var left = ChoiceLayout.Place(2, 0);
        var right = ChoiceLayout.Place(2, 1);
        Assert.Equal((-400f, 40f, 720f, 620f), (left.X, left.Y, left.Width, left.Height));
        Assert.Equal((400f, 40f), (right.X, right.Y));
    }

    [Fact]
    public void ThreeTilesAreTwoOnTopAndTheThirdCentredBelow()
    {
        var a = ChoiceLayout.Place(3, 0);
        var b = ChoiceLayout.Place(3, 1);
        var c = ChoiceLayout.Place(3, 2);
        Assert.Equal(a.Y, b.Y);
        Assert.Equal(-a.X, b.X);
        Assert.True(a.X < 0);
        Assert.Equal(0f, c.X);
        Assert.True(c.Y < a.Y);
    }

    [Fact]
    public void FourTilesAreAFullGrid()
    {
        var bottomLeft = ChoiceLayout.Place(4, 2);
        Assert.Equal(ChoiceLayout.Place(4, 0).X, bottomLeft.X);
        Assert.True(bottomLeft.Y < ChoiceLayout.Place(4, 0).Y);
    }

    // The band runs from 230 above the bottom edge to 100 below the top in the 1080-high reference.
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void GridTilesStayBetweenTheTitleAndTheWarning(int count)
    {
        for (var i = 0; i < count; i++)
        {
            var s = ChoiceLayout.Place(count, i);
            Assert.True(s.Y + s.Height / 2 <= 440.01f, $"tile {i} top");
            Assert.True(s.Y - s.Height / 2 >= -310.01f, $"tile {i} bottom");
            Assert.True(System.Math.Abs(s.X) + s.Width / 2 <= 960f, $"tile {i} side");
        }
    }

    [Theory]
    [InlineData(3, 0, 1, 0, 1)]   // right along the top row
    [InlineData(3, 1, 1, 0, 1)]   // stops at the row's end
    [InlineData(3, 0, 0, 1, 2)]   // down to the centred tile
    [InlineData(3, 1, 0, 1, 2)]   // from either side
    [InlineData(3, 2, 0, -1, 0)]  // up from it
    [InlineData(3, 2, 1, 0, 2)]   // nothing beside it
    [InlineData(4, 1, 0, 1, 3)]
    [InlineData(4, 3, -1, 0, 2)]
    [InlineData(2, 0, 1, 0, 1)]   // two tiles: left and right as before
    [InlineData(2, 1, 0, 1, 1)]   // and up/down does nothing
    public void ArrowsMoveAroundTheGrid(int count, int from, int dx, int dy, int expected)
        => Assert.Equal(expected, ChoiceLayout.Step(count, from, dx, dy));
}
