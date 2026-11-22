#nullable enable
using System;

namespace MapVariants.Shared
{
    /// <summary>
    /// The choice window's pages. Up to four variants show at once exactly as ChoiceLayout places
    /// them; more are split into pages of four. Chevrons, Q/E, Page Up/Down and the dots turn the
    /// view only; the arrow keys move the selection, and walking off a page edge carries it over.
    /// </summary>
    public static class ChoicePaging
    {
        public const int PageSize = 4;

        public static bool Paged(int count) => count > PageSize;

        public static int Pages(int count) => count <= PageSize ? 1 : (count + PageSize - 1) / PageSize;

        public static int PageOf(int index) => index / PageSize;

        public static int OnPage(int count, int page) => Math.Max(0, Math.Min(PageSize, count - page * PageSize));

        // A page of three or four is laid out as itself. A short last page of one or two keeps the
        // four-tile grid's slots, so its tiles are the same size as on every other page.
        public static ChoiceLayout.Slot Place(int count, int index)
        {
            if (!Paged(count)) return ChoiceLayout.Place(count, index);
            var onPage = OnPage(count, PageOf(index));
            return ChoiceLayout.Place(onPage >= 3 ? onPage : PageSize, index % PageSize);
        }

        // Arrow keys. Within a page as ChoiceLayout moves; left or right off the edge of a page turns
        // to the next one, landing on the same row at the near side.
        public static int Step(int count, int index, int dx, int dy)
        {
            if (!Paged(count)) return ChoiceLayout.Step(count, index, dx, dy);
            var page = PageOf(index);
            var start = page * PageSize;
            var moved = start + ChoiceLayout.Step(OnPage(count, page), index - start, dx, dy);
            if (moved != index || dx == 0) return moved;

            var target = page + Math.Sign(dx);
            if (target < 0 || target >= Pages(count)) return index;
            var row = (index - start) / 2;
            var local = row * 2 + (dx > 0 ? 0 : 1);
            return target * PageSize + Math.Min(local, OnPage(count, target) - 1);
        }
    }
}
