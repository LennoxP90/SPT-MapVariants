using MapVariants.Shared;
using Xunit;

namespace MapVariants.Tests;

public class ChoicePagingTests
{
    [Theory]
    [InlineData(2, 1)]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(8, 2)]
    [InlineData(9, 3)]
    public void PagesHoldFour(int count, int pages) => Assert.Equal(pages, ChoicePaging.Pages(count));

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void UpToFourIsLaidOutAsBefore(int count)
    {
        for (var i = 0; i < count; i++)
        {
            var before = ChoiceLayout.Place(count, i);
            var now = ChoicePaging.Place(count, i);
            Assert.Equal((before.X, before.Y, before.Width), (now.X, now.Y, now.Width));
        }
    }

    [Fact]
    public void AShortLastPageKeepsTheGridSlots()
    {
        // Six variants: the second page has two, in the top row of the four-tile grid.
        Assert.Equal(ChoiceLayout.Place(4, 0).X, ChoicePaging.Place(6, 4).X);
        Assert.Equal(ChoiceLayout.Place(4, 1).Y, ChoicePaging.Place(6, 5).Y);
        Assert.Equal(ChoiceLayout.Place(4, 0).Width, ChoicePaging.Place(6, 5).Width);
    }

    [Fact]
    public void AThreeTileLastPageCentresItsOddTile()
        => Assert.Equal(0f, ChoicePaging.Place(7, 6).X);

    [Theory]
    [InlineData(6, 1, 1, 0, 4)]    // right off the top row of page 1 lands top-left of page 2
    [InlineData(6, 3, 1, 0, 5)]    // bottom row, page 2 has only a top row: its last tile
    [InlineData(6, 4, -1, 0, 1)]   // left off page 2 lands top-right of page 1
    [InlineData(6, 5, 1, 0, 5)]    // the last page's right edge stays put
    [InlineData(6, 0, -1, 0, 0)]   // the first page's left edge stays put
    [InlineData(6, 0, 1, 0, 1)]    // inside a page, as before
    [InlineData(6, 0, 0, 1, 2)]
    [InlineData(4, 1, 1, 0, 1)]    // four or fewer never turns
    public void ArrowsWalkOffAPageEdge(int count, int index, int dx, int dy, int expected)
        => Assert.Equal(expected, ChoicePaging.Step(count, index, dx, dy));
}
