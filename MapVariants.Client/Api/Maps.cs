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
        public static bool IsBackport => MapVariant.IsBackport(CurrentVariant);

        public static bool IsManaged(string locationId) => ClientMaps.Set.IsManaged(locationId);

        // The full label, ready to print alone. An unmanaged map gets its location id back.
        public static string DisplayName(string locationId, string variant)
        {
            var map = ClientMaps.Set.For(locationId);
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

        public static event Action<string> VariantResolved
        {
            add => BackportSuspend.Resolved += value;
            remove => BackportSuspend.Resolved -= value;
        }
    }
}
