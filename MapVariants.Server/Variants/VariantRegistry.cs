using JetBrains.Annotations;
using MapVariants.Shared;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Spt.Tables;
using Location = SPTarkov.Server.Core.Models.Eft.Common.Location;

namespace MapVariants.Server.Variants;

/// <summary>
/// Field sets per managed location, one assigned onto the live Location at a time.
/// The stock set is captured at Watermark and the backport set at PostLoad; a
/// first-party mod registers its own pair through Providers, and a source mod adds
/// one further set under its own variant id.
/// </summary>
// Non-interference: Install(backport) assigns the backport's own objects, untouched. Every mirror
// below writes into the stock set only.
[Injectable(InjectionType.Singleton), UsedImplicitly]
public class VariantRegistry(LocationTable locationTable, ISptLogger<VariantRegistry> logger)
{
    private sealed class Entry
    {
        public required LocationFieldSet Original;
        public LocationFieldSet? Backport;
        public required string WireId;

        // The backport's values before any load-time mod configured them, and the object they
        // were read from, so the replay can prove the reading belongs to the backport.
        public LocationValues? PristineBackport;
        public object? PristineSource;

        // Further variants of this location, each owned by its own mod (API 3.2), keyed by variant
        // id. Ordinal, so the resting source is the one the client's ManagedMap.Resting names.
        public readonly SortedDictionary<string, LocationFieldSet> Sources = new(StringComparer.Ordinal);

        // The shipped map's replayable values when each source registered, which is what it was built from. A
        // load-time mod that edits the location after that edits the shipped map, not the source; the difference
        // is replayed onto the source as it is installed, as it is onto the shipped map from a backport.
        public readonly Dictionary<string, LocationValues> SourcePristine = new(StringComparer.Ordinal);

        public bool Managed => Backport is not null || Sources.Count > 0;

        // The backport when there is one, else the source whose id sorts first.
        public string Resting => Backport is not null || Sources.Count == 0 ? MapVariant.Backport : Sources.Keys.First();
    }

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _installed = new(StringComparer.OrdinalIgnoreCase);

    // Which variant put the backport set on the table: "backport" or a declared one built on it.
    // The field sets cannot tell them apart, so the id is remembered at install.
    private readonly Dictionary<string, string> _installedId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    // The client plugin each location's backport belongs to (API 3.3), declared by the backport's own
    // server mod. Kept apart from the entries, which discovery and RegisterProvider rebuild, so a
    // declaration made at any point of the load survives them. Absent means undeclared, and the
    // client then skips its check, exactly as before.
    private readonly Dictionary<string, string> _backportPlugins = new(StringComparer.OrdinalIgnoreCase);

    // Set once discovery has run. A source registered before then is installed by discovery, never
    // at registration, so a backport that builds on the live location later in PostLoad still builds
    // on the shipped data and not on the source's.
    private bool _discovered;

    // Watermark: every location, seven references each. It also forces the LazyLoad accessor's
    // type initialisers, so a layout change surfaces here, not on the first raid that switches.
    internal void SnapshotAll()
    {
        LazyLoadTransformers.VerifyLayout();
        lock (_gate)
        {
            foreach (var (key, location) in locationTable.GetDictionary())
            {
                if (location?.Base is null) continue;
                var id = Key(key);
                _entries[id] = new Entry
                {
                    Original = LocationFieldSet.Capture(location),
                    WireId = location.Base?.Id ?? id,
                };
            }
            logger.Debug($"[MV] snapshot: {_entries.Count} location(s) captured at Watermark");
        }
    }

    /// <summary>
    /// Reads each backport's values, for every location that has one and has not been read yet.
    /// Called from several probes across the load: the first reading taken once a location stopped
    /// being the shipped object is kept and frozen, because a later probe would read a copy a
    /// load-time mod has already changed.
    /// </summary>
    internal void CaptureBackportValues()
    {
        lock (_gate)
        {
            int read = 0, frozen = 0, shipped = 0;
            foreach (var pair in _entries)
            {
                var entry = pair.Value;
                if (entry.PristineSource is not null) { frozen++; continue; }

                var live = locationTable.GetLocation(pair.Key);
                if (live?.Base is null) continue;

                // Still the object captured at Watermark, so no backport has replaced it yet.
                if (ReferenceEquals(live.Base, entry.Original.Base)) { shipped++; continue; }

                entry.PristineBackport = LocationValueMirror.Read(live.Base);
                entry.PristineSource = live.Base;
                read++;
            }
            if (read > 0)
                logger.Debug($"[MV] pristine values read for {read} backport(s); {frozen} already read, "
                             + $"{shipped} still on the shipped data");
        }
    }

    // PostLoad: whichever locations changed have a backport, and everything else is dropped so it
    // can never be installed.
    internal void DiscoverAll()
    {
        lock (_gate)
        {
            var found = new List<string>();
            foreach (var (key, location) in locationTable.GetDictionary())
            {
                var id = Key(key);
                // The "already has a backport" half of this guard is what keeps a REGISTERED provider
                // safe: without it, discovery would capture the provider's backport as the stock set
                // and the two variants would become the same map.
                if (!_entries.TryGetValue(id, out var entry) || entry.Backport is not null) continue;
                // A source's set may already be live if it registered after an earlier discovery. It
                // is that source's own data, never a backport, so it is not captured as one.
                if (entry.Sources.Values.Any(set => set.IsLive(location))) continue;
                var now = LocationFieldSet.Capture(location);
                if (!DiscoveryPolicy.HasBackport(entry.Original.References, now.References)) continue;
                entry.Backport = now;
                found.Add(id);
                var changed = DiscoveryPolicy.ChangedIndices(entry.Original.References, now.References);
                logger.Info($"[MV] {entry.WireId}: backport discovered, {changed.Length} of 7 field(s) replaced; switchable if a client asks for it");
                if (entry.Sources.Count > 0)
                    logger.Warning($"[MV] {entry.WireId}: that backport sits beside source(s) {string.Join(", ", entry.Sources.Keys)}, so the table "
                                   + "rests on it. If no client plugin offers it (maps.json or Register), a mod that only edits this location "
                                   + "at PostLoad was taken for a backport, and clients will rest on a source the server is not serving.");
            }
            // A location with a source stays even with no backport: its stock set is what a Vanilla
            // raid there installs. Everything else is dropped so it can never be installed.
            var toDrop = _entries.Keys.Where(id => !_entries[id].Managed).ToList();
            foreach (var id in toDrop) _entries.Remove(id);

            // Sources registered before this point were held off the table. Each location now rests
            // on its resting variant; with a backport, that is the backport, which is already live.
            foreach (var (id, entry) in _entries)
            {
                if (entry.Sources.Count == 0) continue;
                if (entry.Backport is null) InstallKeyed(id, entry.Resting);
                logger.Info($"[MV] {entry.WireId}: variants {string.Join(", ", Offered(entry))} offered; the table rests on {entry.Resting}");
            }
            _discovered = true;
            if (_entries.Count == 0) logger.Info("[MV] no backported location found; nothing to switch");
            logger.Debug($"[MV] discovery dropped {toDrop.Count} location(s) with no backport or source; {_entries.Count} managed");
        }
    }

    private static IEnumerable<string> Offered(Entry entry)
        => new[] { MapVariant.Original }
           .Concat(entry.Backport is not null ? new[] { MapVariant.Backport } : Array.Empty<string>())
           .Concat(entry.Sources.Keys);

    /// <summary>
    /// A source mod's own variant of a location, beside whatever else offers one (API 3.2). The
    /// stock set is always the one captured at Watermark, never anything the caller holds, so
    /// every source and the backport switch against the same shipped map.
    /// </summary>
    public bool RegisterSource(string locationId, string variantId, Location variant)
    {
        var id = Key(locationId);
        var variantKey = (variantId ?? "").Trim().ToLowerInvariant();
        lock (_gate)
        {
            if (!_entries.TryGetValue(id, out var entry))
            {
                logger.Error($"[MV] source '{variantKey}' for '{locationId}' refused: no such location was captured at Watermark");
                return false;
            }
            if (variant?.Base is null)
            {
                logger.Error($"[MV] source '{variantKey}' for '{entry.WireId}' refused: its location has no base");
                return false;
            }
            if (!MapVariant.Declare(variantKey))
            {
                logger.Error($"[MV] source '{variantKey}' for '{entry.WireId}' refused: the id is empty or reserved");
                return false;
            }
            if (entry.Sources.ContainsKey(variantKey))
            {
                logger.Error($"[MV] source '{variantKey}' for '{entry.WireId}' refused: another mod already registered that id there");
                return false;
            }

            entry.Sources[variantKey] = LocationFieldSet.Capture(variant);
            entry.SourcePristine[variantKey] = LocationValueMirror.Read(entry.Original.Base);

            // Before discovery the table keeps whatever it holds and discovery installs the resting
            // variant. After it, only a location with nothing else to rest on is installed now.
            if (_discovered && entry.Backport is null && entry.Resting == variantKey) InstallKeyed(id, variantKey);
            logger.Info(_discovered
                ? $"[MV] {entry.WireId}: source '{variantKey}' registered after discovery"
                : $"[MV] {entry.WireId}: source '{variantKey}' registered; the table is set at discovery");
            return true;
        }
    }

    /// <summary>
    /// The shipped location as captured at Watermark, as a new Location object holding the stock
    /// field set. For a source mod that builds its variant from the shipped map: by PostLoad the
    /// live location may already hold another mod's backport. Null for a location not captured.
    /// The objects are SPT's own, shared with the live table: copy before changing anything.
    /// </summary>
    public Location? Shipped(string locationId)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(Key(locationId), out var entry)) return null;
            var copy = new Location();
            entry.Original.AssignTo(copy);
            return copy;
        }
    }

    // The variant a location rests on between raids: the backport, or its first source.
    public string Resting(string locationId)
    {
        lock (_gate) return _entries.TryGetValue(Key(locationId), out var e) && e.Managed ? e.Resting : MapVariant.Backport;
    }

    /// <summary>
    /// First-party providers hand over their own pair. ONE LOCATION ID PER CALL,
    /// because each has its own Location object in SPT; several ids sharing one
    /// player-facing choice is a client concern, ManagedMap.LocationIds.
    /// </summary>
    public void RegisterProvider(string locationId, Location original, Location backport)
    {
        var id = Key(locationId);
        lock (_gate)
        {
            // Sources registered on this location survive a provider registering its pair after them.
            var sources = _entries.TryGetValue(id, out var previous) ? previous.Sources : null;
            _entries[id] = new Entry
            {
                Original = LocationFieldSet.Capture(original),
                Backport = LocationFieldSet.Capture(backport),
                WireId = original.Base?.Id ?? locationId.Trim().ToLowerInvariant(),
                // Registration runs ahead of the load-time mods, so this pair is pristine too.
                PristineBackport = LocationValueMirror.Read(backport.Base),
                PristineSource = backport.Base,
            };
            if (sources is not null)
                foreach (var (key, set) in sources) _entries[id].Sources[key] = set;
            // Installed immediately, which is what makes the resting state true by construction. A
            // provider's table holds whatever that mod last assigned, which we cannot know.
            InstallKeyed(id, MapVariant.Backport);
            logger.Info(_installed.Contains(id)
                ? $"[MV] {_entries[id].WireId}: registered provider, its backport installed; the table rests on it"
                : $"[MV] {_entries[id].WireId}: registered provider, but its backport could NOT be installed; "
                  + "the table rests on whatever that mod left there and a raid nobody chose for may be served the wrong data");
        }
    }

    /// <summary>
    /// API 3.3: which CLIENT plugin (its BepInEx GUID) loads this location's backport scenes. The
    /// variant word alone cannot say which mod is "the backport", so a client whose backport is a
    /// different mod's would load its scenes over this server's data. Sent on every variant answer;
    /// the client compares it with its own maps.json pluginGuid. False for an empty GUID.
    /// </summary>
    public bool DeclarePlugin(string locationId, string clientPluginGuid)
    {
        var guid = (clientPluginGuid ?? "").Trim();
        if (guid.Length == 0)
        {
            logger.Error($"[MV] backport plugin for '{locationId}' refused: the client plugin GUID is empty");
            return false;
        }
        lock (_gate)
        {
            var id = Key(locationId);
            if (_backportPlugins.TryGetValue(id, out var previous) && !string.Equals(previous, guid, StringComparison.OrdinalIgnoreCase))
                logger.Warning($"[MV] {id}: backport plugin {previous} replaced by {guid}; two mods each declare themselves this map's backport");
            _backportPlugins[id] = guid;
            logger.Info($"[MV] {id}: the backport is client plugin {guid}");
            return true;
        }
    }

    // The client plugin behind what the table serves now: the declared one while the backport (or a
    // variant built on it) is live, else null. A source's id names its own mod; the shipped map has none.
    public string? ServedPlugin(string locationId)
    {
        var id = Key(locationId);
        // Read first and outside the gate: InstalledVariant takes it itself.
        var served = InstalledVariant(locationId);
        lock (_gate)
        {
            if (!_backportPlugins.TryGetValue(id, out var guid) || !_entries.TryGetValue(id, out var e)) return null;
            return e.Sources.ContainsKey(served) || !MapVariant.IsBackport(served) ? null : guid;
        }
    }

    public bool IsManaged(string locationId)
    {
        lock (_gate) return _entries.TryGetValue(Key(locationId), out var e) && e.Managed;
    }

    // Materialised inside the lock: a lazy enumerable would escape it and could throw "collection
    // was modified" if a provider registers while a caller is still enumerating.
    public IEnumerable<string> ManagedIds
    {
        get
        {
            lock (_gate) return _entries.Values.Where(e => e.Managed).Select(e => e.WireId).ToList();
        }
    }

    public void Install(string locationId, string variant)
    {
        lock (_gate) InstallKeyed(Key(locationId), variant);
    }

    // The one install path, so registration and every raid hook do the same bookkeeping. The caller
    // holds _gate and has already mapped the id through Key.
    private void InstallKeyed(string id, string variant)
    {
        if (!_entries.TryGetValue(id, out var entry) || !entry.Managed)
        {
            logger.Debug($"[MV] install '{variant}' for '{id}' ignored: not a managed location");
            return;
        }
        var live = locationTable.GetLocation(id);
        if (live is null)
        {
            logger.Debug($"[MV] install '{variant}' for '{id}' ignored: no such location in the table");
            return;
        }

        var wanted = MapVariant.Normalise(variant);
        // Loading-screen banners are not map data: other mods add theirs to whichever copy is live (ISB Aishi at
        // PostLoad + 90100), so the list stays with the table and only the map is switched under it.
        var banners = live.Base?.Banners;

        // On a location with no backport, any id that is not a source here means the resting
        // source, exactly as the client's ManagedMap.OwnerOf reads it.
        if (MapVariant.IsBackport(wanted) && entry.Backport is null && !entry.Sources.ContainsKey(wanted))
            wanted = entry.Resting;

        if (entry.Sources.TryGetValue(wanted, out var source))
        {
            // The source's own objects, as the backport's below, with what load-time mods configured since it
            // registered replayed onto it first.
            ReplayConfigIntoSource(id, entry, wanted, source);
            source.AssignTo(live);
            _installedId[id] = wanted;
            logger.Info($"[MV] {id}: serving source '{wanted}'");
        }
        else if (MapVariant.IsBackport(wanted))
        {
            entry.Backport!.AssignTo(live);
            _installedId[id] = wanted;
        }
        else
        {
            // Assign first, mirror second: the mirror writes into entry.Original's own lazy objects,
            // the ones just assigned onto live. If it throws, the table is left on the shipped map
            // without the mirrored transformers rather than stuck on the backport.
            entry.Original.AssignTo(live);
            MirrorTransformersIntoStock(id, entry);
            ReplayConfigIntoStock(id, entry);
            logger.Info($"[MV] {id}: serving the SHIPPED map; its own spawn configuration is in use, not the backport's");
        }

        if (banners is not null && live.Base is not null) live.Base.Banners = banners;
        _installed.Add(id);
    }

    // Installed, as opposed to merely discovered. The raid-end reset only applies where an install
    // happened, so a location another mod manages is never touched.
    public bool HasInstalled(string locationId)
    {
        lock (_gate) return _installed.Contains(Key(locationId));
    }

    // Copied into the STOCK set only, never the other way. Waves and boss spawns are deliberately
    // NOT mirrored: see docs/DESIGN-NOTES.md before adding anything here.
    private void MirrorTransformersIntoStock(string id, Entry entry)
    {
        // A location only sources offer has no backport to mirror from.
        if (entry.Backport is null) return;
        var copied = LazyLoadTransformers.MirrorMissing(entry.Backport!.LooseLoot, entry.Original.LooseLoot)
                   + LazyLoadTransformers.MirrorMissing(entry.Backport.StaticContainers, entry.Original.StaticContainers)
                   + LazyLoadTransformers.MirrorMissing(entry.Backport.StaticLoot, entry.Original.StaticLoot);
        if (copied > 0) logger.Debug($"[MV] {id}: mirrored {copied} LazyLoad transformer(s) into the stock set");
    }

    // What load-time mods changed on the shipped map after a source was built from it: a spawn mod later in PostLoad,
    // which on a backport edited the installed backport. Same rules as the replay onto the shipped map below.
    private void ReplayConfigIntoSource(string id, Entry entry, string key, LocationFieldSet source)
    {
        if (!entry.SourcePristine.TryGetValue(key, out var pristine)) return;
        try
        {
            var plan = ReplayPolicy.Compute(pristine, LocationValueMirror.Read(entry.Original.Base));
            if (plan.IsEmpty) return;
            var outcome = LocationValueMirror.Apply(source.Base, plan);
            var summary = $"[MV] {id}: replayed {outcome.Written} of {plan.Count} value(s) a load-time mod configured "
                          + $"onto source '{key}': {plan.Describe()}";
            if (outcome.Refused.Count == 0) logger.Info(summary);
            else logger.Warning(summary + " | NOT written: " + string.Join(", ", outcome.Refused));
        }
        catch (Exception ex)
        {
            logger.Error($"[MV] {id}: config replay onto source '{key}' failed; it serves the values it was built with", ex);
        }
    }

    // A backport is installed during Preload, so a mod that configures locations at load only ever
    // configures the backport. Diffing it against the pristine reading gives what those mods
    // changed, and only that is written onto the shipped map.
    private void ReplayConfigIntoStock(string id, Entry entry)
    {
        if (entry.PristineBackport is null || entry.Backport is null)
        {
            logger.Debug($"[MV] {id}: no pristine reading, so no config replay");
            return;
        }

        // The reading is only pristine if it came from the object that turned out to be the
        // backport. Anything else would copy the backport's own data across.
        if (!ReferenceEquals(entry.PristineSource, entry.Backport!.Base))
        {
            logger.Info($"[MV] {id}: the pristine reading is not of this backport's base, so no "
                        + "configuration is replayed; the shipped map serves its own values");
            return;
        }

        // Swallowed for the same reason as the mirror above: a refused replay must leave the table
        // on the shipped map, not stranded on the backport.
        try
        {
            var plan = ReplayPolicy.Compute(entry.PristineBackport, LocationValueMirror.Read(entry.Backport!.Base));
            if (plan.IsEmpty)
            {
                logger.Info($"[MV] {id}: no load-time mod changed this map's replayable values, so nothing is replayed");
                return;
            }

            var outcome = LocationValueMirror.Apply(entry.Original.Base, plan);
            var summary = $"[MV] {id}: replayed {outcome.Written} of {plan.Count} value(s) a load-time mod "
                          + $"configured onto the shipped map: {plan.Describe()}";
            if (outcome.Refused.Count == 0) logger.Info(summary);
            else logger.Warning(summary + " | NOT written: " + string.Join(", ", outcome.Refused));
        }
        catch (Exception ex)
        {
            logger.Error($"[MV] {id}: config replay failed; the shipped map serves its own values", ex);
        }
    }

    public string InstalledVariant(string locationId)
    {
        var id = Key(locationId);
        lock (_gate)
        {
            var live = locationTable.GetLocation(id);
            if (live is null || !_entries.TryGetValue(id, out var e) || !e.Managed) return MapVariant.Original;
            foreach (var (key, set) in e.Sources)
                if (set.IsLive(live)) return key;
            if (e.Backport is null || !e.Backport.IsLive(live)) return MapVariant.Original;
            return _installedId.TryGetValue(id, out var declared) && !e.Sources.ContainsKey(declared) ? declared : MapVariant.Backport;
        }
    }

    public string Installed(string locationId)
    {
        var id = Key(locationId);
        lock (_gate)
        {
            var live = locationTable.GetLocation(id);
            if (live is null) return "no location in the table";
            if (!_entries.TryGetValue(id, out var e) || !e.Managed) return "not managed";
            var source = e.Sources.FirstOrDefault(pair => pair.Value.IsLive(live)).Key;
            var which = source is not null ? source.ToUpperInvariant()
                : e.Backport is not null && e.Backport.IsLive(live) ? (_installedId.TryGetValue(id, out var v) ? v : MapVariant.Backport).ToUpperInvariant()
                : e.Original.IsLive(live) ? "ORIGINAL" : "NONE OF THEM - something else replaced Base";
            return $"{which}, {live.Base?.SpawnPointParams?.Count() ?? -1} spawn point(s), {live.Base?.Exits?.Count() ?? -1} exit(s)";
        }
    }

    // DELIBERATELY NOT LocationId.Normalise, and not to be folded into it: this one also maps through
    // the table, which turns factory4_day into factory4day. Merging the two breaks Factory.
    private string Key(string key) => locationTable.GetMappedKey(key.Trim()).ToLowerInvariant();
}
