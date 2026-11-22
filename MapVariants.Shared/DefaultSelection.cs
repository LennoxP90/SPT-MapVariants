#nullable enable
using System;
using System.Linq;

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
        // back to the map SPT ships, the same as the default. A map declaring the same name twice
        // resolves to the backport.
        public static string ToVariant(string? stored, string? backportName, string? originalName)
        {
            var value = (stored ?? "").Trim();
            if (Matches(value, backportName)) return MapVariant.Backport;
            return MapVariant.Original;
        }

        // A declared variant's name is checked after the two, and a source's after those, so a name
        // it shares with them loses. Anything unrecognised is the map SPT ships.
        public static string ToVariant(string? stored, ManagedMap map)
        {
            var value = (stored ?? "").Trim();
            if (!map.HasBackport)
            {
                if (Matches(value, map.OriginalName)) return MapVariant.Original;
                return map.Sources.FirstOrDefault(x => Matches(value, x.Name))?.Id ?? MapVariant.Original;
            }
            if (Matches(value, map.BackportName) || Matches(value, map.OriginalName))
                return ToVariant(stored, map.BackportName, map.OriginalName);
            var extra = map.Extras.FirstOrDefault(x => Matches(value, x.Name));
            if (extra != null) return extra.Id;
            var source = map.Sources.FirstOrDefault(x => Matches(value, x.Name));
            return source != null ? source.Id : ToVariant(stored, map.BackportName, map.OriginalName);
        }

        static bool Matches(string value, string? name)
        {
            var trimmed = (name ?? "").Trim();
            return trimmed.Length > 0 && value.Equals(trimmed, StringComparison.OrdinalIgnoreCase);
        }
    }
}
