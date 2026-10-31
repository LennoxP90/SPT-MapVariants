#nullable enable
using System;

namespace MapVariants.Shared
{
    /// <summary>
    /// Where each tile of the map choice window sits, in the window's 1920x1080 reference space, as an
    /// offset from the centre. Two tiles keep the original side-by-side layout; more go in a grid two
    /// wide, with an odd last tile centred under the row above.
    /// </summary>
    public static class ChoiceLayout
    {
        public const float PairSpacing = 800f, PairWidth = 720f, PairHeight = 620f, PairY = 40f;

        // The band between the title and the warning line: 750 high, centred 65 above the middle.
        const float BandHeight = 750f, BandY = 65f, Gap = 10f, Columns = 2f;

        // The picture fills the top 70% of a tile at the image's shape, 1024x594.
        const float PictureShare = 0.7f, PictureAspect = 1024f / 594f;

        public struct Slot
        {
            public float X, Y, Width, Height;
        }

        public static int Rows(int count) => count <= 2 ? 1 : (count + 1) / 2;

        public static Slot Place(int count, int index)
        {
            if (count < 1 || index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index));
            if (count <= 2)
                return new Slot { X = (index - (count - 1) / 2f) * PairSpacing, Y = PairY, Width = PairWidth, Height = PairHeight };

            var rows = Rows(count);
            var height = (BandHeight - Gap * (rows - 1)) / rows;
            var width = height * PictureShare * PictureAspect;
            var row = index / 2;
            var alone = index == count - 1 && count % 2 == 1;
            var column = index % 2;
            var x = alone ? 0f : (column - (Columns - 1) / 2f) * (width + 4 * Gap);
            var y = BandY + ((rows - 1) / 2f - row) * (height + Gap);
            return new Slot { X = x, Y = y, Width = width, Height = height };
        }

        // Arrow keys: left and right move along a row, up and down between rows. The centred odd tile
        // is reached from either tile above it. Stops at the edges.
        public static int Step(int count, int index, int dx, int dy)
        {
            if (count <= 2) return Math.Max(0, Math.Min(count - 1, index + dx));
            var target = index + dx + 2 * dy;
            if (dx != 0 && target / 2 != index / 2) return index;
            if (dy > 0 && target >= count) target = count - 1;
            return target < 0 || target >= count ? index : target;
        }
    }
}
