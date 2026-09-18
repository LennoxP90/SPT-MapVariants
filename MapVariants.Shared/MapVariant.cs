#nullable enable
using System;

namespace MapVariants.Shared
{
    /// <summary>
    /// Which dataset the server serves and which scenes the client loads, under
    /// one location id. The words match FactoryClassic and InterchangeRework.
    /// </summary>
    public static class MapVariant
    {
        public const string Original = "original";
        public const string Backport = "backport";

        // A third wire answer, not a variant: Normalise folds it to "original" like any other
        // unrecognised word, so nothing downstream shifts. See docs/DESIGN-NOTES.md.
        public const string Unmanaged = "unmanaged";

        public static bool IsUnmanaged(string? variant)
            => string.Equals((variant ?? "").Trim(), Unmanaged, StringComparison.OrdinalIgnoreCase);

        public static string Normalise(string? variant)
            => string.Equals((variant ?? "").Trim(), Backport, StringComparison.OrdinalIgnoreCase) ? Backport : Original;

        public static bool IsBackport(string? variant) => Normalise(variant) == Backport;
    }
}
