using System;
using MapVariants.Shared;

namespace MapVariants.Client.Api
{
    /// <summary>
    /// The public surface, read by reflection after a soft BepInDependency on this
    /// plugin, so everything here is a const, a primitive property, a method over
    /// primitives or an Action of string. See docs/DESIGN-NOTES.md.
    /// </summary>
    // Named Maps, not MapVariants: a class named after the root namespace shadows
    // MapVariants.Shared inside every file of this namespace.
    public static class Maps
    {
        public const string Original = MapVariant.Original;
        public const string Backport = MapVariant.Backport;

        public static string ApiVersion => ExtensionApiContract.Version;

        // NULL MEANS "NOT DECIDED YET", NEVER "ORIGINAL".
        public static string CurrentVariant => BackportSuspend.SessionChoice;

        public static string CurrentLocationId => BackportSuspend.SessionLocationId;
        public static bool Decided => CurrentVariant != null;
        // True for every variant except Vanilla: the backport, anything declared on top of it, and
        // every source's variant. Use IsVariant to tell those apart.
        public static bool IsBackport => MapVariant.IsBackport(CurrentVariant);

        // False until decided, and for an id this raid is not, including one nobody declared.
        public static bool IsVariant(string variant)
            => string.Equals(CurrentVariant, (variant ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

        // Every variant the map offers, original and backport first, sources last. Empty for an
        // unmanaged map.
        public static string[] Variants(string locationId) => ClientMaps.For(locationId)?.Variants ?? new string[0];

        public static bool IsManaged(string locationId) => ClientMaps.Set.IsManaged(locationId);

        // API 3.3: which mod's variant this raid loaded, in words other mods can key on. SelectedId is
        // "owner:variant": the plugin GUID that registered the variant and that mod's own id for it,
        // such as "com.manimal.lighthouse:manimal". The map SPT ships is "spt:shipped". Null until
        // decided, and for a map MapVariants does not manage. CurrentVariant is unchanged.
        public static string SelectedId => IdFor(CurrentLocationId, CurrentVariant);
        public static string SelectedOwner => Decided ? ClientMaps.Identity(CurrentLocationId, CurrentVariant)?.Owner : null;
        public static string SelectedName => Decided ? DisplayName(CurrentLocationId, CurrentVariant) : null;

        // The same id for any variant of any map, such as one from Variants(locationId), so a lookup can
        // be built before a raid. Null for an unmanaged map or a null variant.
        public static string IdFor(string locationId, string variant)
        {
            if (variant == null) return null;
            var identity = ClientMaps.Identity(locationId, variant);
            return identity == null ? null : VariantIdentity.Compose(identity.Value.Owner, identity.Value.Variant);
        }

        // The full label, ready to print alone. An unmanaged map gets its location id back.
        public static string DisplayName(string locationId, string variant)
        {
            var map = ClientMaps.For(locationId);
            return map == null ? locationId : VariantDisplay.For(map, variant);
        }

        /// <summary>
        /// For a first-party mod that owns its own variant. Call from Awake, after a
        /// hard BepInDependency on this plugin; onVariant is invoked before the map's
        /// preset loads. False means the map is NOT managed and the caller owns it.
        /// </summary>
        /// <param name="backportName">The variant's name ALONE: "Classic", never "Factory - Classic", or every composed label doubles the map name.</param>
        /// <param name="backportWarnings">Called on the UI thread just before the window is built, so it must not throw or block.</param>
        public static bool Register(string[] locationIds, string displayName, string backportName, string originalName,
                                    string backportTile, string originalTile, Action<string> onVariant,
                                    Func<string[]> backportWarnings)
            => ClientMaps.Register(locationIds, displayName, backportName, originalName, backportTile, originalTile,
                                   onVariant, backportWarnings);

        /// <summary>
        /// Register with further variants beyond the two (API 3.1). Each extra is built on the
        /// backport: the server serves the backport's data and the backport's client patches stay
        /// live, and the caller adds its own content on top. Ids are lower-cased and must not be
        /// "original", "backport" or "unmanaged". A separate name, not a Register overload, so a
        /// consumer looking Register up by name alone still finds exactly one.
        /// </summary>
        /// <param name="extraTiles">May be null; otherwise the same length as extraIds.</param>
        public static bool RegisterVariants(string[] locationIds, string displayName, string backportName, string originalName,
                                            string backportTile, string originalTile, string[] extraIds, string[] extraNames,
                                            string[] extraTiles, Action<string> onVariant, Func<string[]> backportWarnings)
            => ClientMaps.Register(locationIds, displayName, backportName, originalName, backportTile, originalTile,
                                   onVariant, backportWarnings, extraIds, extraNames, extraTiles);

        /// <summary>
        /// API 3.2: for a mod that owns its own variant of a map that another mod may also offer one
        /// of. It joins the map already managed for those ids (a backport, a provider, or other
        /// sources), or creates one with only this variant beside the original. Call from Awake,
        /// after a hard BepInDependency on this plugin, and register the same variantId on the
        /// server through Providers.RegisterSource.
        /// onVariant is told in two words whatever else the map offers: "backport" when this
        /// variant is chosen, "original" for anything else, the shipped map and other mods'
        /// variants alike. It is invoked before the map's preset loads. False means refused: the
        /// id is empty, reserved, or already offered on that map, and the log says which.
        /// </summary>
        /// <param name="variantId">The wire word, such as "classic". Lower-cased; not original, backport or unmanaged.</param>
        /// <param name="variantName">The variant's name ALONE, such as "Classic".</param>
        /// <param name="displayName">Used only when this call creates the map; an existing map keeps its name.</param>
        /// <param name="originalName">Used only when this call creates the map.</param>
        /// <param name="warnings">Shown on this variant's tile only. Called on the UI thread; must not throw or block.</param>
        public static bool RegisterSource(string[] locationIds, string displayName, string variantId, string variantName,
                                          string originalName, string variantTile, string originalTile,
                                          Action<string> onVariant, Func<string[]> warnings)
            => ClientMaps.RegisterSource(locationIds, displayName, variantId, variantName, originalName, variantTile, originalTile,
                                         onVariant, warnings);

        /// <summary>
        /// API 3.3: a maps.json entry made at runtime, for a backport whose plugin loads after MapVariants and
        /// would otherwise have to edit maps.json and ask for a restart. Same fields as the file's entry; the
        /// server needs nothing, as for maps.json. Call from Awake. True when the map is now offered for this
        /// plugin (also when it already was); false when refused, and the log says why.
        /// </summary>
        /// <param name="standDown">Null, "none", or "Namespace.Type:MethodName", as in maps.json.</param>
        /// <param name="backportTile">Optional absolute path of the tile picture; null uses tiles/ as for maps.json.</param>
        public static bool RegisterBackport(string[] locationIds, string pluginGuid, string displayName, string backportName,
                                            string originalName, string standDown, string backportTile, string originalTile)
            => ClientMaps.RegisterBackport(locationIds, pluginGuid, displayName, backportName, originalName, standDown,
                                           backportTile, originalTile);

        public static event Action<string> VariantResolved
        {
            add => BackportSuspend.Resolved += value;
            remove => BackportSuspend.Resolved -= value;
        }
    }
}
