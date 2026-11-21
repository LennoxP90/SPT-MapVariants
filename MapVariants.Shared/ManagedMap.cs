#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace MapVariants.Shared
{
    /// <summary>
    /// One map a player can choose a variant for. Several location ids may share one
    /// choice: Factory's day and night are one map and one prompt. PluginGuid is null
    /// for a registered provider and set for a discovered one. Sources are further mods
    /// that each own a variant of the same map, beside the backport or with none.
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

        // Variants a provider declared beyond the two, each built on the backport. Empty for every
        // discovered map and for a provider that registered through the two-variant overload.
        public ExtraVariant[] Extras { get; }

        // False for a map only sources offer: no maps.json entry and no Register call claimed it, so
        // there is no "backport" variant, no BackportName and no plugin whose patches get suspended.
        public bool HasBackport { get; }

        // Further mods that each own a variant of this map (API 3.2), in registration order. Each is
        // switched by its own callback; none of them is the backport or built on it.
        public VariantSource[] Sources { get; }

        // Display order, left to right: original, backport, the extras as declared, then the sources.
        public string[] Variants
            => new[] { MapVariant.Original }
               .Concat(HasBackport ? new[] { MapVariant.Backport }.Concat(Extras.Select(x => x.Id)) : new string[0])
               .Concat(Sources.Select(x => x.Id)).ToArray();

        // What the server's table rests on and what "no answer" means: the backport when there is
        // one, else the source whose id sorts first. Ordinal, never registration order, because the
        // server and the client see the sources arrive in different orders.
        public string Resting
            => HasBackport || Sources.Length == 0 ? MapVariant.Backport
             : Sources.Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal).First();

        // Which owner a choice belongs to: original, backport (the backport and every extra built on
        // it), or a source's id. An id this map does not offer falls to the backport, as it always
        // has, or to the resting source on a map with no backport.
        public string OwnerOf(string? variant)
        {
            var id = MapVariant.Normalise(variant);
            if (id == MapVariant.Original) return MapVariant.Original;
            var source = Sources.FirstOrDefault(x => x.Id == id);
            if (source != null) return source.Id;
            return HasBackport ? MapVariant.Backport : Resting;
        }

        // The variant a choice actually selects on this map: the choice itself when the map offers
        // it, otherwise the owner OwnerOf settles on.
        public string Offered(string? variant)
        {
            var id = MapVariant.Normalise(variant);
            return Variants.Contains(id) ? id : OwnerOf(id);
        }

        public VariantSource? SourceFor(string? variant)
        {
            var owner = OwnerOf(variant);
            return Sources.FirstOrDefault(x => x.Id == owner);
        }

        public string NameFor(string? variant)
        {
            var id = MapVariant.Normalise(variant);
            if (id == MapVariant.Original) return OriginalName;
            var source = SourceFor(id);
            if (source != null) return source.Name;
            return id == MapVariant.Backport ? BackportName : Extras.FirstOrDefault(x => x.Id == id)?.Name ?? BackportName;
        }

        public string? TileFor(string? variant)
        {
            var id = MapVariant.Normalise(variant);
            if (id == MapVariant.Original) return OriginalTile;
            var source = SourceFor(id);
            if (source != null) return source.Tile;
            return id == MapVariant.Backport ? BackportTile : Extras.FirstOrDefault(x => x.Id == id)?.Tile ?? BackportTile;
        }

        // null, "none", or "Namespace.Type:MethodName". docs/DESIGN-NOTES.md has why one backport
        // needs a stand-down and another does not.
        public string? StandDown { get; }

        public StandDownDeclaration Declaration =>
            StandDown == null ? StandDownDeclaration.Undeclared
            : string.Equals(StandDown, "none", StringComparison.OrdinalIgnoreCase) ? StandDownDeclaration.Safe
            : StandDownDeclaration.Target;

        public ManagedMap(string[] locationIds, string? pluginGuid, string backportName, string originalName,
                          string? backportTile = null, string? originalTile = null, string? standDown = null,
                          string? displayName = null, ExtraVariant[]? extras = null)
            : this(locationIds, pluginGuid, backportName, originalName, backportTile, originalTile, standDown,
                   displayName, extras, true, null)
        {
        }

        ManagedMap(string[] locationIds, string? pluginGuid, string backportName, string originalName,
                   string? backportTile, string? originalTile, string? standDown,
                   string? displayName, ExtraVariant[]? extras, bool hasBackport, VariantSource[]? sources)
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
            Extras = (extras ?? new ExtraVariant[0]).Where(x => x != null && MapVariant.Declare(x.Id))
                                                    .GroupBy(x => x.Id).Select(g => g.First()).ToArray();
            HasBackport = hasBackport;
            if (!hasBackport) { PluginGuid = null; StandDown = null; BackportTile = null; Extras = new ExtraVariant[0]; }

            // One id, one owner: a source may not reuse an id the backport side or another source offers.
            var taken = new HashSet<string>(Extras.Select(x => x.Id), StringComparer.Ordinal);
            Sources = (sources ?? new VariantSource[0]).Where(x => x != null && MapVariant.Declare(x.Id) && taken.Add(x.Id)).ToArray();
            if (!hasBackport && Sources.Length == 0) throw new ArgumentException("a map with no backport needs at least one source", nameof(sources));
        }

        /// <summary>A map only sources offer: no backport, and this source as its one variant beside the original.</summary>
        public static ManagedMap ForSource(string[] locationIds, string? displayName, string? originalName,
                                           string? originalTile, VariantSource source)
            => new ManagedMap(locationIds, null, "", originalName ?? "", null, originalTile, null, displayName, null, false,
                              new[] { source ?? throw new ArgumentNullException(nameof(source)) });

        // Whether a source could join this map: a usable id, not reserved, not already offered here.
        public bool CanAdd(VariantSource? source)
            => source != null && source.Id.Length > 0 && source.Id != MapVariant.Original && source.Id != MapVariant.Backport
               && source.Id != MapVariant.Unmanaged && !Variants.Contains(source.Id);

        /// <summary>This map with one more source. Throws if the id is reserved or already offered here.</summary>
        public ManagedMap WithSource(VariantSource source)
        {
            if (!CanAdd(source)) throw new ArgumentException($"'{Key}' already offers a variant '{source?.Id}', or that id is reserved", nameof(source));
            return new ManagedMap(LocationIds, PluginGuid, BackportName, OriginalName, BackportTile, OriginalTile, StandDown,
                                  DisplayName, Extras, HasBackport, Sources.Concat(new[] { source }).ToArray());
        }

        /// <summary>
        /// A backport (from maps.json or Register) joining a map sources already offer: the backport's
        /// fields with that map's sources added. The map name and the original's name stay the
        /// backport's, which is what a player saw before any source existed.
        /// </summary>
        public ManagedMap WithSourcesOf(ManagedMap other)
        {
            if (!HasBackport) throw new InvalidOperationException("only a map with a backport takes another map's sources");
            var sources = Sources.ToList();
            foreach (var s in other?.Sources ?? new VariantSource[0])
                if (!sources.Any(x => x.Id == s.Id)) sources.Add(s);
            return new ManagedMap(LocationIds, PluginGuid, BackportName, OriginalName, BackportTile, OriginalTile, StandDown,
                                  DisplayName, Extras, true, sources.ToArray());
        }

        /// <summary>
        /// This map as offered when its backport plugin is not loaded but sources are: the sources
        /// alone. The server found no backport there either, so it rests on a source too.
        /// </summary>
        public ManagedMap WithoutBackport()
            => !HasBackport || Sources.Length == 0 ? this
             : new ManagedMap(LocationIds, null, "", OriginalName, null, OriginalTile, null, DisplayName, null, false, Sources);

        public bool Covers(string? locationId) => LocationIds.Contains(Normalise(locationId));

        // Lets a caller build the map first and read the real Key off it: the Key is the first
        // NON-EMPTY id, which is not always the first id.
        public ManagedMap WithTiles(string? backportTile, string? originalTile)
            => new ManagedMap(LocationIds, PluginGuid, BackportName, OriginalName, backportTile, originalTile, StandDown, DisplayName, Extras,
                              HasBackport, Sources);

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

    /// <summary>One declared variant beyond the two. Id is lower-cased; Name is the variant alone.</summary>
    public sealed class ExtraVariant
    {
        public string Id { get; }
        public string Name { get; }
        public string? Tile { get; }

        public ExtraVariant(string id, string? name, string? tile = null)
        {
            Id = (id ?? "").Trim().ToLowerInvariant();
            Name = string.IsNullOrWhiteSpace(name) ? Id : name!.Trim();
            Tile = string.IsNullOrWhiteSpace(tile) ? null : tile;
        }
    }

    /// <summary>
    /// A mod that owns its own variant of a map beside the backport (API 3.2), switched by its own
    /// callback. Id is lower-cased and is that variant's wire word; Name is the variant alone.
    /// </summary>
    public sealed class VariantSource
    {
        public string Id { get; }
        public string Name { get; }
        public string? Tile { get; }

        public VariantSource(string id, string? name, string? tile = null)
        {
            Id = (id ?? "").Trim().ToLowerInvariant();
            Name = string.IsNullOrWhiteSpace(name) ? Id : name!.Trim();
            Tile = string.IsNullOrWhiteSpace(tile) ? null : tile;
        }
    }
}
