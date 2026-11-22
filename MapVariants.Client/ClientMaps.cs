using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using MapVariants.Shared;

namespace MapVariants.Client
{
    /// <summary>
    /// The managed set on this client: every maps.json entry, plus first-party
    /// providers that registered, plus sources that each own a further variant of
    /// a map. One F12 DefaultSelection per map, bound here whether or not its
    /// backport plugin turns out to be loaded, and bound again when a source joins.
    /// </summary>
    internal static class ClientMaps
    {
        internal static readonly ManagedMapSet Set = new ManagedMapSet();
        static readonly Dictionary<string, ConfigEntry<string>> Defaults = new Dictionary<string, ConfigEntry<string>>();
        static readonly Dictionary<string, Action<string>> Callbacks = new Dictionary<string, Action<string>>();

        // The assembly each registration's callback lives in, keyed "<map key>/<source id>" ("" for the
        // backport). Resolved to a plugin GUID only when asked: during the caller's Awake BepInEx has not
        // finished recording that plugin.
        static readonly Dictionary<string, System.Reflection.Assembly> Owners = new Dictionary<string, System.Reflection.Assembly>();

        // Beside Callbacks rather than on ManagedMap, which is pure data source-linked into the tests.
        static readonly Dictionary<string, Func<string[]>> Warnings = new Dictionary<string, Func<string[]>>();

        // Per map key, then per source id: each source's own callback and warning supplier.
        static readonly Dictionary<string, Dictionary<string, Action<string>>> SourceCallbacks = new Dictionary<string, Dictionary<string, Action<string>>>();
        static readonly Dictionary<string, Dictionary<string, Func<string[]>>> SourceWarnings = new Dictionary<string, Dictionary<string, Func<string[]>>>();

        static readonly HashSet<string> MissingReported = new HashSet<string>();
        static ConfigFile _config;
        internal static ConfigEntry<bool> PromptSelection;

        internal static bool Prompt => PromptSelection.Value;

        internal static void Install(ConfigFile config, string pluginDir)
        {
            _config = config;
            PromptSelection = config.Bind("General", "PromptSelection", true, new ConfigDescription(
                "Ask which version of a map to load when one with a backport installed is picked on the map screen. "
                + "The answer applies to that raid only. Off = never ask, and each map loads its DefaultSelection.",
                null, new ConfigurationManagerAttributes { Order = 100 }));

            foreach (var map in MapsFile.Load(Path.Combine(pluginDir, "maps.json")))
                Add(map);
        }

        /// <summary>
        /// The map as this client offers it now. A map whose backport plugin is not loaded but which
        /// sources also offer is the sources alone: the server found no backport there either. Every
        /// decision path asks through here rather than through Set.
        /// </summary>
        internal static ManagedMap For(string locationId)
        {
            var map = Set.For(locationId);
            if (map == null || !map.HasBackport || map.Sources.Length == 0 || BackportPluginPresent(map)) return map;
            return map.WithoutBackport();
        }

        // Reflection-friendly: strings, string arrays and a framework delegate. It returns false
        // rather than throwing back into a third party's Awake. See docs/DESIGN-NOTES.md.
        internal static bool Register(string[] locationIds, string displayName, string backportName, string originalName,
                                      string backportTile, string originalTile, Action<string> onVariant,
                                      Func<string[]> backportWarnings,
                                      string[] extraIds = null, string[] extraNames = null, string[] extraTiles = null)
        {
            try
            {
                if (Refused(locationIds, backportName, originalName, onVariant)) return false;
                if ((extraIds?.Length ?? 0) != (extraNames?.Length ?? 0) || (extraTiles != null && extraTiles.Length != extraIds.Length))
                {
                    Plugin.Log.LogError($"[MV] Register refused for '{string.Join(",", locationIds)}': extraIds, extraNames and extraTiles must be the same length");
                    return false;
                }

                var extras = (extraIds ?? new string[0]).Select((id, i) => new ExtraVariant(id, extraNames[i], extraTiles?[i])).ToArray();
                var map = new ManagedMap(locationIds, null, backportName, originalName, backportTile, originalTile,
                                         displayName: displayName, extras: extras);

                // One statement group: a throw between them would leave a managed map with a callback
                // and no warnings, or a map nothing on this client can swap.
                map = Add(map);
                Callbacks[map.Key] = onVariant;
                Owners[map.Key + "/"] = onVariant.Method.DeclaringType?.Assembly;
                if (backportWarnings != null) Warnings[map.Key] = backportWarnings;
                Plugin.Log.LogInfo($"[MV] {map.Key}: registered provider ({string.Join(",", map.LocationIds)}), variants {string.Join(",", map.Variants)}");
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[MV] Register failed, that map is not managed on this client: {e}");
                return false;
            }
        }

        /// <summary>
        /// API 3.2: one mod's own variant of a map, beside the backport and any other source. It
        /// joins the map already managed for those location ids, or creates one with no backport.
        /// onVariant is told "backport" when this variant is chosen and "original" for any other.
        /// </summary>
        internal static bool RegisterSource(string[] locationIds, string displayName, string variantId, string variantName,
                                            string originalName, string variantTile, string originalTile,
                                            Action<string> onVariant, Func<string[]> warnings)
        {
            try
            {
                if (Refused(locationIds, variantName, originalName ?? "", onVariant)) return false;
                var source = new VariantSource(variantId, variantName, variantTile);
                if (source.Id.Length == 0 || !MapVariant.Declare(source.Id))
                {
                    Plugin.Log.LogError($"[MV] RegisterSource refused for '{string.Join(",", locationIds)}': variant id '{variantId}' is empty or reserved");
                    return false;
                }

                var existing = Set.Exactly(locationIds);
                ManagedMap map;
                if (existing != null)
                {
                    if (!existing.CanAdd(source))
                    {
                        Plugin.Log.LogError($"[MV] RegisterSource refused for '{existing.Key}': it already offers a variant '{source.Id}'");
                        return false;
                    }
                    map = existing.WithSource(source);
                    Set.Replace(existing, map);
                }
                else if (Set.AnyManaged(locationIds))
                {
                    Plugin.Log.LogError($"[MV] RegisterSource refused for '{string.Join(",", locationIds)}': those ids are split across "
                                        + "managed maps or cover only part of one; a source joins a whole map");
                    return false;
                }
                else
                {
                    map = ManagedMap.ForSource(locationIds, displayName, originalName, originalTile, source);
                    Set.Add(map);
                }

                if (!SourceCallbacks.TryGetValue(map.Key, out var callbacks)) SourceCallbacks[map.Key] = callbacks = new Dictionary<string, Action<string>>();
                callbacks[source.Id] = onVariant;
                Owners[map.Key + "/" + source.Id] = onVariant.Method.DeclaringType?.Assembly;
                if (warnings != null)
                {
                    if (!SourceWarnings.TryGetValue(map.Key, out var suppliers)) SourceWarnings[map.Key] = suppliers = new Dictionary<string, Func<string[]>>();
                    suppliers[source.Id] = warnings;
                }
                Defaults[map.Key] = BindDefault(map);
                Plugin.Log.LogInfo($"[MV] {map.Key}: registered source '{source.Id}' ({string.Join(",", map.LocationIds)}), variants {string.Join(",", map.Variants)}");
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[MV] RegisterSource failed, that variant is not offered on this client: {e}");
                return false;
            }
        }

        /// <summary>
        /// API 3.3: a maps.json entry made at runtime, for a backport whose plugin loads after this one. It goes
        /// through the same parser and registration as the file, so it behaves exactly as if the entry had been
        /// there at startup. An entry already managed for the same plugin is not an error; any other claim on
        /// those ids is refused, as a second maps.json entry would be.
        /// </summary>
        internal static bool RegisterBackport(string[] locationIds, string pluginGuid, string displayName, string backportName,
                                              string originalName, string standDown, string backportTile, string originalTile)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(pluginGuid))
                {
                    Plugin.Log.LogError($"[MV] RegisterBackport refused for '{string.Join(",", locationIds ?? new string[0])}': pluginGuid is required");
                    return false;
                }
                if (Refused(locationIds, backportName, originalName, _ => { })) return false;

                var entry = new Newtonsoft.Json.Linq.JObject
                {
                    ["locationIds"] = new Newtonsoft.Json.Linq.JArray(locationIds),
                    ["pluginGuid"] = pluginGuid,
                    ["displayName"] = displayName,
                    ["backportName"] = backportName,
                    ["originalName"] = originalName,
                    ["standDown"] = standDown,
                };
                var maps = MapsFile.Parse(new Newtonsoft.Json.Linq.JObject { ["maps"] = new Newtonsoft.Json.Linq.JArray(entry) }.ToString());
                if (maps.Length != 1)
                {
                    Plugin.Log.LogError($"[MV] RegisterBackport refused for '{string.Join(",", locationIds)}': the entry did not parse");
                    return false;
                }

                var map = backportTile == null && originalTile == null ? maps[0] : maps[0].WithTiles(backportTile, originalTile);
                var existing = Set.Exactly(map.LocationIds);
                if (existing != null && existing.HasBackport
                    && string.Equals(existing.PluginGuid, map.PluginGuid, StringComparison.OrdinalIgnoreCase))
                {
                    Plugin.Log.LogInfo($"[MV] {existing.Key}: RegisterBackport for {map.PluginGuid}, already managed for it");
                    return true;
                }

                map = Add(map);
                Plugin.Log.LogInfo($"[MV] {map.Key}: backport registered at runtime for {map.PluginGuid}, variants {string.Join(",", map.Variants)}");
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[MV] RegisterBackport failed, that backport is not offered on this client: {e.Message}");
                return false;
            }
        }

        // Everything is checked before any state is touched, because this is the least controlled
        // input in the mod: called by reflection, from a third party, with whatever it passes.
        static bool Refused(string[] locationIds, string backportName, string originalName, Action<string> onVariant)
        {
            if (locationIds == null || locationIds.All(string.IsNullOrWhiteSpace))
            {
                Plugin.Log.LogError("[MV] Register refused: locationIds must hold at least one non-empty location id");
                return true;
            }
            if (onVariant == null)
            {
                Plugin.Log.LogError($"[MV] Register refused for '{string.Join(",", locationIds)}': onVariant is null, so nothing could swap the map");
                return true;
            }
            if (backportName == null || originalName == null)
            {
                Plugin.Log.LogError($"[MV] Register refused for '{string.Join(",", locationIds)}': both variant names are required");
                return true;
            }
            return false;
        }

        // A backport (maps.json or Register) onto ids only sources offer so far takes those sources
        // with it. Any other second claim on a location throws before anything is bound, as it always
        // has. Returns the map as it now stands in the set. FumsCustoms calls this by reflection, so
        // keep the name and the single ManagedMap parameter.
        static ManagedMap Add(ManagedMap map)
        {
            var existing = Set.Exactly(map.LocationIds);
            if (existing != null && !existing.HasBackport && map.HasBackport)
            {
                var merged = map.WithSourcesOf(existing);
                Set.Replace(existing, merged);
                Defaults[merged.Key] = BindDefault(merged);
                Plugin.Log.LogInfo($"[MV] {merged.Key}: backport joined the source(s) already offered, variants {string.Join(",", merged.Variants)}");
                return merged;
            }

            // Throws for a location already managed, before BindDefault could rebind that map's entry.
            if (Set.AnyManaged(map.LocationIds)) Set.Add(map);

            var entry = BindDefault(map);
            Set.Add(map);
            Defaults[map.Key] = entry;
            return map;
        }

        // A string with an AcceptableValueList, not an enum: an enum is one type shared by every
        // section, so it could only ever offer one pair of words for every map. Bound again when a
        // source joins, because BepInEx hands back an existing entry with its old value list; the
        // player's stored pick is always carried across, even a name not offered yet: sources join one
        // at a time, so a pick of the second would otherwise be reset when the first joins.
        static ConfigEntry<string> BindDefault(ManagedMap map)
        {
            var section = map.DisplayName;
            string kept = null;
            if (Defaults.TryGetValue(map.Key, out var previous))
            {
                kept = previous.Value;
                _config.Remove(previous.Definition);
            }

            // The backport's name first, then the original's, as the list has always been ordered.
            var names = map.Variants.Where(v => v == MapVariant.Backport)
                           .Concat(map.Variants.Where(v => v != MapVariant.Backport))
                           .Select(map.NameFor).Distinct().ToArray();
            var entry = _config.Bind(section, "DefaultSelection", map.OriginalName, new ConfigDescription(
                $"Which {section} to load when PromptSelection is Off, and what you offer when you are first into a "
                + "transit heading there.",
                new LenientValueList(names),
                new ConfigurationManagerAttributes { Order = 50 }));
            if (kept != null && entry.Value != kept) entry.Value = kept;
            return entry;
        }

        // The dropdown's list, without the clamp. A map is bound from maps.json before a source has
        // registered, and a stock list would reset a stored source name ("Classic") to the first
        // entry and save that, so the player's pick would be lost on every start. DefaultSelection
        // already reads any unknown name as the map SPT ships.
        sealed class LenientValueList : AcceptableValueList<string>
        {
            public LenientValueList(params string[] values) : base(values) { }
            public override object Clamp(object value) => value;
            public override bool IsValid(object value) => value is string;
        }

        // Read at the point of use, never cached, so ConfigReload applies.
        internal static string DefaultFor(ManagedMap map)
            => DefaultSelection.ToVariant(Defaults.TryGetValue(map.Key, out var e) ? e.Value : null, map);

        /// <summary>The descriptive owner and variant of a choice on this location, or null when it is unmanaged.</summary>
        internal static (string Owner, string Variant)? Identity(string locationId, string variant)
        {
            var map = For(locationId);
            if (map == null) return null;
            return VariantIdentity.For(map, variant, PluginOf(map.Key + "/"), id => PluginOf(map.Key + "/" + id));
        }

        static string PluginOf(string ownerKey)
        {
            if (!Owners.TryGetValue(ownerKey, out var assembly) || assembly == null) return null;
            var path = assembly.Location;
            return Chainloader.PluginInfos.Values.FirstOrDefault(i => string.Equals(i.Location, path, StringComparison.OrdinalIgnoreCase))?.Metadata.GUID;
        }

        // The backport's own callback: null for a maps.json backport and for a map with no backport.
        internal static Action<string> Callback(ManagedMap map)
            => map.HasBackport && Callbacks.TryGetValue(map.Key, out var cb) ? cb : null;

        internal static Func<string[]> BackportWarnings(ManagedMap map)
            => Warnings.TryGetValue(map.Key, out var w) ? w : null;

        // Each source's id and callback on this map, in the map's own source order.
        internal static IEnumerable<KeyValuePair<string, Action<string>>> SourceCallbacksFor(ManagedMap map)
        {
            if (!SourceCallbacks.TryGetValue(map.Key, out var callbacks)) yield break;
            foreach (var source in map.Sources)
                if (callbacks.TryGetValue(source.Id, out var cb)) yield return new KeyValuePair<string, Action<string>>(source.Id, cb);
        }

        // Whose warnings a tile shows: the backport's for the backport and its extras, a source's for
        // its own variant, and none for the shipped map.
        internal static Func<string[]> WarningsFor(ManagedMap map, string variant)
        {
            var owner = map.OwnerOf(variant);
            if (owner == MapVariant.Original) return null;
            if (owner == MapVariant.Backport) return BackportWarnings(map);
            return SourceWarnings.TryGetValue(map.Key, out var suppliers) && suppliers.TryGetValue(owner, out var w) ? w : null;
        }

        // Asked when it matters, never at startup: BepInEx loads alphabetically and this plugin
        // sorts before the backports, so a check in Awake drops every map (docs/DESIGN-NOTES.md).
        internal static bool BackportPluginPresent(ManagedMap map)
            => map.PluginGuid == null || Chainloader.PluginInfos.ContainsKey(map.PluginGuid);

        internal static bool ReportIfBackportMissing(ManagedMap map)
        {
            if (BackportPluginPresent(map)) return false;
            if (MissingReported.Add(map.Key))
                Plugin.Log.LogWarning($"[MV] {map.Key}: its backport plugin {map.PluginGuid} is not loaded; "
                                    + "this map is left to whatever the server serves and no choice is offered");
            return true;
        }

        // "seen" rather than "installed": a third party's backport can read "not seen yet" here at
        // startup and work perfectly, because presence is asked again at every use.
        internal static string Summary()
        {
            var parts = new List<string>();
            foreach (var m in Set.All)
            {
                var pick = VariantDisplay.NameFor(m, DefaultFor(m));
                var seen = !m.HasBackport ? "sources only" : m.PluginGuid == null ? "provider" : BackportPluginPresent(m) ? "backport seen" : "backport NOT seen yet";
                var sources = m.Sources.Length == 0 ? "" : ", sources " + string.Join("+", m.Sources.Select(s => s.Id));
                parts.Add($"{m.Key}={pick} ({seen}{sources})");
            }
            return parts.Count == 0 ? "no managed maps in maps.json" : string.Join(", ", parts);
        }
    }
}
