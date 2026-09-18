#nullable enable
using System;

namespace MapVariants.Shared
{
    /// <summary>
    /// Maps the F12 DefaultSelection string back to a wire variant. It holds one of
    /// a map's own two variant names, not a fixed word, because a BepInEx enum is
    /// one type for every section.
    /// </summary>
    public static class DefaultSelection
    {
        // Tolerant on purpose: a player edits this file by hand, so anything unrecognised falls
        // back to the backport, and a map declaring the same name twice resolves to it.
        public static string ToVariant(string? stored, string? backportName, string? originalName)
        {
            var value = (stored ?? "").Trim();
            if (value.Length == 0) return MapVariant.Backport;
            if (Matches(value, backportName)) return MapVariant.Backport;
            if (Matches(value, originalName)) return MapVariant.Original;
            return MapVariant.Backport;
        }

        public static string ToVariant(string? stored, ManagedMap map)
            => ToVariant(stored, map.BackportName, map.OriginalName);

        static bool Matches(string value, string? name)
        {
            var trimmed = (name ?? "").Trim();
            return trimmed.Length > 0 && value.Equals(trimmed, StringComparison.OrdinalIgnoreCase);
        }
    }
}
