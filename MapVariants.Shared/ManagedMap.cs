#nullable enable
using System;
using System.Linq;

namespace MapVariants.Shared
{
    /// <summary>
    /// One map a player can choose a variant for. Several location ids may share one
    /// choice: Factory's day and night are one map and one prompt. PluginGuid is null
    /// for a registered provider and set for a discovered one.
    /// </summary>
    public sealed class ManagedMap
    {
        public string Key { get; }
        public string[] LocationIds { get; }

        // The capitalised Key fallback reads well for "lighthouse" and not for "factory4_day".
        public string DisplayName { get; }

        public string? PluginGuid { get; }

        // Each variant's name ALONE, with no map name in it; VariantDisplay composes that in.
        public string BackportName { get; }
        public string OriginalName { get; }

        public string? BackportTile { get; }
        public string? OriginalTile { get; }

        // null, "none", or "Namespace.Type:MethodName". docs/DESIGN-NOTES.md has why one backport
        // needs a stand-down and another does not.
        public string? StandDown { get; }

        public StandDownDeclaration Declaration =>
            StandDown == null ? StandDownDeclaration.Undeclared
            : string.Equals(StandDown, "none", StringComparison.OrdinalIgnoreCase) ? StandDownDeclaration.Safe
            : StandDownDeclaration.Target;

        public ManagedMap(string[] locationIds, string? pluginGuid, string backportName, string originalName,
                          string? backportTile = null, string? originalTile = null, string? standDown = null,
                          string? displayName = null)
        {
            if (locationIds == null || locationIds.Length == 0) throw new ArgumentException("a managed map needs at least one location id", nameof(locationIds));
            LocationIds = locationIds.Select(Normalise).Where(id => id.Length > 0).Distinct().ToArray();
            if (LocationIds.Length == 0) throw new ArgumentException("a managed map needs at least one non-empty location id", nameof(locationIds));
            Key = LocationIds[0];
            // Only the fallbacks are capitalised: a declared name is used exactly as written.
            DisplayName = Named(displayName, Capitalise(Key));
            PluginGuid = string.IsNullOrWhiteSpace(pluginGuid) ? null : pluginGuid!.Trim();
            BackportName = Named(backportName, "Retail");
            OriginalName = Named(originalName, "Vanilla");
            BackportTile = backportTile;
            OriginalTile = originalTile;
            StandDown = NormaliseStandDown(standDown);
        }

        public bool Covers(string? locationId) => LocationIds.Contains(Normalise(locationId));

        // Lets a caller build the map first and read the real Key off it: the Key is the first
        // NON-EMPTY id, which is not always the first id.
        public ManagedMap WithTiles(string? backportTile, string? originalTile)
            => new ManagedMap(LocationIds, PluginGuid, BackportName, OriginalName, backportTile, originalTile, StandDown, DisplayName);

        public static string Normalise(string? locationId) => LocationId.Normalise(locationId);

        static string Named(string? declared, string fallback)
            => string.IsNullOrWhiteSpace(declared) ? fallback : declared!.Trim();

        static string? NormaliseStandDown(string? standDown)
        {
            var trimmed = string.IsNullOrWhiteSpace(standDown) ? null : standDown!.Trim();
            return trimmed != null && trimmed.Equals("none", StringComparison.OrdinalIgnoreCase) ? "none" : trimmed;
        }

        static string Capitalise(string key)
            => key.Length == 0 ? key : char.ToUpperInvariant(key[0]) + key.Substring(1);
    }
}
