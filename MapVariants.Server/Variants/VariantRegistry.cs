using JetBrains.Annotations;
using MapVariants.Shared;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Spt.Tables;
using Location = SPTarkov.Server.Core.Models.Eft.Common.Location;

namespace MapVariants.Server.Variants;

/// <summary>
/// Two field sets per managed location, one assigned onto the live Location at a
/// time. The stock set is captured at Watermark and the backport set at PostLoad;
/// a first-party mod registers its own pair through Providers.
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
    }

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _installed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

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
                var now = LocationFieldSet.Capture(location);
                if (!DiscoveryPolicy.HasBackport(entry.Original.References, now.References)) continue;
                entry.Backport = now;
                found.Add(id);
                var changed = DiscoveryPolicy.ChangedIndices(entry.Original.References, now.References);
                logger.Info($"[MV] {entry.WireId}: backport discovered, {changed.Length} of 7 field(s) replaced; switchable if a client asks for it");
            }
            var toDrop = _entries.Keys.Where(id => _entries[id].Backport is null).ToList();
            foreach (var id in toDrop) _entries.Remove(id);
            if (found.Count == 0) logger.Info("[MV] no backported location found; nothing to switch");
            logger.Debug($"[MV] discovery dropped {toDrop.Count} location(s) with no backport; {found.Count} managed");
        }
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
            _entries[id] = new Entry
            {
                Original = LocationFieldSet.Capture(original),
                Backport = LocationFieldSet.Capture(backport),
                WireId = original.Base?.Id ?? locationId.Trim().ToLowerInvariant(),
                // Registration runs ahead of the load-time mods, so this pair is pristine too.
                PristineBackport = LocationValueMirror.Read(backport.Base),
                PristineSource = backport.Base,
            };
            // Installed immediately, which is what makes the resting state true by construction. A
            // provider's table holds whatever that mod last assigned, which we cannot know.
            InstallKeyed(id, MapVariant.Backport);
            logger.Info(_installed.Contains(id)
                ? $"[MV] {_entries[id].WireId}: registered provider, its backport installed; the table rests on it"
                : $"[MV] {_entries[id].WireId}: registered provider, but its backport could NOT be installed; "
                  + "the table rests on whatever that mod left there and a raid nobody chose for may be served the wrong data");
        }
    }

    public bool IsManaged(string locationId)
    {
        lock (_gate) return _entries.TryGetValue(Key(locationId), out var e) && e.Backport is not null;
    }

    // Materialised inside the lock: a lazy enumerable would escape it and could throw "collection
    // was modified" if a provider registers while a caller is still enumerating.
    public IEnumerable<string> ManagedIds
    {
        get
        {
            lock (_gate) return _entries.Values.Where(e => e.Backport is not null).Select(e => e.WireId).ToList();
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
        if (!_entries.TryGetValue(id, out var entry) || entry.Backport is null)
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

        if (MapVariant.IsBackport(variant))
        {
            entry.Backport.AssignTo(live);
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
        var copied = LazyLoadTransformers.MirrorMissing(entry.Backport!.LooseLoot, entry.Original.LooseLoot)
                   + LazyLoadTransformers.MirrorMissing(entry.Backport.StaticContainers, entry.Original.StaticContainers)
                   + LazyLoadTransformers.MirrorMissing(entry.Backport.StaticLoot, entry.Original.StaticLoot);
        if (copied > 0) logger.Debug($"[MV] {id}: mirrored {copied} LazyLoad transformer(s) into the stock set");
    }

    // A backport is installed during Preload, so a mod that configures locations at load only ever
    // configures the backport. Diffing it against the pristine reading gives what those mods
    // changed, and only that is written onto the shipped map.
    private void ReplayConfigIntoStock(string id, Entry entry)
    {
        if (entry.PristineBackport is null)
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
            if (live is null || !_entries.TryGetValue(id, out var e) || e.Backport is null) return MapVariant.Original;
            return e.Backport.IsLive(live) ? MapVariant.Backport : MapVariant.Original;
        }
    }

    public string Installed(string locationId)
    {
        var id = Key(locationId);
        lock (_gate)
        {
            var live = locationTable.GetLocation(id);
            if (live is null) return "no location in the table";
            if (!_entries.TryGetValue(id, out var e) || e.Backport is null) return "not managed";
            var which = e.Backport.IsLive(live) ? "BACKPORT" : e.Original.IsLive(live) ? "ORIGINAL" : "NEITHER - something else replaced Base";
            return $"{which}, {live.Base?.SpawnPointParams?.Count() ?? -1} spawn point(s), {live.Base?.Exits?.Count() ?? -1} exit(s)";
        }
    }

    // DELIBERATELY NOT LocationId.Normalise, and not to be folded into it: this one also maps through
    // the table, which turns factory4_day into factory4day. Merging the two breaks Factory.
    private string Key(string key) => locationTable.GetMappedKey(key.Trim()).ToLowerInvariant();
}
