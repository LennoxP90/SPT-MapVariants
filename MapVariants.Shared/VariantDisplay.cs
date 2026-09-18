#nullable enable
namespace MapVariants.Shared
{
    /// <summary>
    /// Two forms of a variant's name. NameFor is the variant alone ("Manimal"),
    /// for the F12 dropdown, which sits under a heading that is already the map
    /// name; For is the full label, for tiles, loading screen and notifications.
    /// </summary>
    public static class VariantDisplay
    {
        // Composed, never stored, so the map name is declared exactly once. Either side blank drops
        // the separator with it: " - Manimal" reads worse than the one name that is known.
        public static string Compose(string? displayName, string? variantName)
        {
            var map = (displayName ?? "").Trim();
            var variant = (variantName ?? "").Trim();
            if (map.Length == 0) return variant;
            if (variant.Length == 0) return map;
            return map + " - " + variant;
        }

        public static string NameFor(ManagedMap map, string? variant)
            => MapVariant.IsBackport(variant) ? map.BackportName : map.OriginalName;

        public static string For(ManagedMap map, string? variant)
            => Compose(map.DisplayName, NameFor(map, variant));
    }
}
