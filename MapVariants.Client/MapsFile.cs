#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using MapVariants.Shared;
using Newtonsoft.Json;

namespace MapVariants.Client
{
    /// <summary>
    /// maps.json beside the client DLL: which plugin owns which map, what the map
    /// is called, and what to call each of its two variants. The one thing nothing
    /// can discover.
    /// </summary>
    public static class MapsFile
    {
        sealed class Root
        {
            [JsonProperty("maps")] public List<Entry>? Maps { get; set; }
        }

        sealed class Entry
        {
            [JsonProperty("locationIds")] public string[]? LocationIds { get; set; }
            [JsonProperty("pluginGuid")] public string? PluginGuid { get; set; }

            // Each variant's name ALONE, with no map name in it. Absent means "Retail" and "Vanilla".
            [JsonProperty("backportName")] public string? BackportName { get; set; }
            [JsonProperty("originalName")] public string? OriginalName { get; set; }

            // Optional. The map's own name, in the prompt title, the F12 section and every composed
            // label. Absent means the map's capitalised key.
            [JsonProperty("displayName")] public string? DisplayName { get; set; }

            // Optional. Absent, "none", or "Namespace.Type:MethodName". Absent refuses a shipped-map
            // raid once this backport has run; see docs/DESIGN-NOTES.md.
            [JsonProperty("standDown")] public string? StandDown { get; set; }
        }

        public static ManagedMap[] Load(string path)
            => File.Exists(path) ? Parse(File.ReadAllText(path)) : new ManagedMap[0];

        // Anything that is not "none" and not a Type:Method shape degrades to null, which reads as
        // Undeclared and refuses. It must never degrade to "none", which would proceed.
        static string? NormaliseStandDown(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var trimmed = raw!.Trim();
            if (trimmed.Equals("none", StringComparison.OrdinalIgnoreCase)) return "none";
            return StandDownTarget.Parse(trimmed) == null ? null : trimmed;
        }

        public static ManagedMap[] Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new ManagedMap[0];
            var root = JsonConvert.DeserializeObject<Root>(json);
            var result = new List<ManagedMap>();
            foreach (var entry in root?.Maps ?? new List<Entry>())
            {
                if (entry?.LocationIds == null || entry.LocationIds.Length == 0) continue;

                ManagedMap map;
                try
                {
                    map = new ManagedMap(entry.LocationIds, entry.PluginGuid,
                                         entry.BackportName ?? "", entry.OriginalName ?? "",
                                         standDown: NormaliseStandDown(entry.StandDown), displayName: entry.DisplayName);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                // Off map.Key, never off locationIds[0]: the key is the first NON-EMPTY id, and
                // deriving the paths from the raw first id put the tiles under a different name.
                result.Add(map.WithTiles($"tiles/{map.Key}-backport.png", $"tiles/{map.Key}-original.png"));
            }
            return result.ToArray();
        }
    }
}
