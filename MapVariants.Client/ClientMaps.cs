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
    /// providers that registered. One F12 DefaultSelection per map, bound here
    /// whether or not its backport plugin turns out to be loaded.
    /// </summary>
    internal static class ClientMaps
    {
        internal static readonly ManagedMapSet Set = new ManagedMapSet();
        static readonly Dictionary<string, ConfigEntry<string>> Defaults = new Dictionary<string, ConfigEntry<string>>();
        static readonly Dictionary<string, Action<string>> Callbacks = new Dictionary<string, Action<string>>();

        // Beside Callbacks rather than on ManagedMap, which is pure data source-linked into the tests.
        static readonly Dictionary<string, Func<string[]>> Warnings = new Dictionary<string, Func<string[]>>();
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

        // Reflection-friendly: strings, string arrays and a framework delegate. It returns false
        // rather than throwing back into a third party's Awake. See docs/DESIGN-NOTES.md.
        internal static bool Register(string[] locationIds, string displayName, string backportName, string originalName,
                                      string backportTile, string originalTile, Action<string> onVariant,
                                      Func<string[]> backportWarnings)
        {
            try
            {
                if (Refused(locationIds, backportName, originalName, onVariant)) return false;

                var map = new ManagedMap(locationIds, null, backportName, originalName, backportTile, originalTile,
                                         displayName: displayName);
                var entry = BindDefault(map);

                // One statement group: a throw between them would leave a managed map with a callback
                // and no warnings, or a map nothing on this client can swap.
                Set.Add(map);
                Defaults[map.Key] = entry;
                Callbacks[map.Key] = onVariant;
                if (backportWarnings != null) Warnings[map.Key] = backportWarnings;
                Plugin.Log.LogInfo($"[MV] {map.Key}: registered provider ({string.Join(",", map.LocationIds)})");
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[MV] Register failed, that map is not managed on this client: {e}");
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

        static void Add(ManagedMap map)
        {
            var entry = BindDefault(map);
            Set.Add(map);
            Defaults[map.Key] = entry;
        }

        // A string with an AcceptableValueList, not an enum: an enum is one type shared by every
        // section, so it could only ever offer one pair of words for every map.
        static ConfigEntry<string> BindDefault(ManagedMap map)
        {
            var section = map.DisplayName;
            return _config.Bind(section, "DefaultSelection", map.BackportName, new ConfigDescription(
                $"Which {section} to load when PromptSelection is Off, and what you offer when you are first into a "
                + "transit heading there.",
                new AcceptableValueList<string>(map.BackportName, map.OriginalName),
                new ConfigurationManagerAttributes { Order = 50 }));
        }

        // Read at the point of use, never cached, so ConfigReload applies.
        internal static string DefaultFor(ManagedMap map)
            => DefaultSelection.ToVariant(Defaults.TryGetValue(map.Key, out var e) ? e.Value : null, map);

        internal static Action<string> Callback(ManagedMap map)
            => Callbacks.TryGetValue(map.Key, out var cb) ? cb : null;

        internal static Func<string[]> BackportWarnings(ManagedMap map)
            => Warnings.TryGetValue(map.Key, out var w) ? w : null;

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
                var seen = m.PluginGuid == null ? "provider" : BackportPluginPresent(m) ? "backport seen" : "backport NOT seen yet";
                parts.Add($"{m.Key}={pick} ({seen})");
            }
            return parts.Count == 0 ? "no managed maps in maps.json" : string.Join(", ", parts);
        }
    }
}
