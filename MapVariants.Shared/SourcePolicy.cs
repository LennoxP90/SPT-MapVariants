#nullable enable

namespace MapVariants.Shared
{
    /// <summary>
    /// How one choice reaches each mod that owns a variant of the same map. Every owner
    /// is told in its own two words: its variant (backport, or the backport's extra that
    /// was chosen) when the choice is its own, original when it belongs to anyone else.
    /// A source never hears another source's id, so a consumer written against the
    /// two-variant vocabulary keeps working unchanged.
    /// </summary>
    public static class SourcePolicy
    {
        // What the backport's own callback is handed: the chosen variant exactly as before when the
        // backport side owns it, so a map with no sources behaves as it always has.
        public static string ForBackport(ManagedMap map, string? chosen)
        {
            var id = map.Offered(chosen);
            return map.HasBackport && map.OwnerOf(id) == MapVariant.Backport ? id : MapVariant.Original;
        }

        // What a source's callback is handed: "backport" for its own variant, "original" otherwise.
        public static string ForSource(ManagedMap map, string sourceId, string? chosen)
            => map.OwnerOf(chosen) == (sourceId ?? "").Trim().ToLowerInvariant() ? MapVariant.Backport : MapVariant.Original;

        // Whether the backport's patches on the scene load must be live for this choice. False on a
        // map with no backport, and false whenever a source owns the choice: the backport loading
        // its own scenes over a source's raid is exactly what the suspension prevents.
        public static bool BackportWanted(ManagedMap map, string? chosen)
            => map.HasBackport && map.OwnerOf(chosen) == MapVariant.Backport;
    }
}
