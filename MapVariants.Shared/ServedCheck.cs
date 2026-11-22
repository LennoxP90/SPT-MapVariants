#nullable enable
using System;
using System.Linq;

namespace MapVariants.Shared
{
    /// <summary>What the server says it serves, set against what this client can load.</summary>
    public enum ServedVerdict
    {
        // The client has what the server serves: the shipped map, one of this map's sources, or the
        // backport of the same plugin.
        Match,

        // Nothing to compare: no answer, an unmanaged answer, or a backport whose plugin one side
        // did not declare. The client carries on exactly as before this check existed.
        Unverified,

        // The server serves a variant id this map does not offer here, such as a source whose client
        // plugin is not installed. MapVariant.Normalise folds it to "original", so without this check
        // the client would load the shipped scenes over that source's data.
        UnknownVariant,

        // The server serves the backport, and this client has no backport for this map.
        NoBackportHere,

        // The server's backport belongs to a different client plugin than this client's backport:
        // two mods each think they are "the backport" of one map.
        BackportOfAnotherMod,
    }

    /// <summary>
    /// The variant word alone cannot say WHICH mod is behind it. "backport" on the server may be one
    /// mod's data while "backport" on this client loads another mod's scenes, and a source id this
    /// client never registered reads as "original". Either way the raid loads one map's scenes over
    /// another map's data, and nothing fails loudly: containers with no loot, missing exits, bots off
    /// the navmesh. This settles whether the two sides agree.
    /// </summary>
    public static class ServedCheck
    {
        /// <param name="map">The map as this client offers it now (ClientMaps.For).</param>
        /// <param name="served">The variant word exactly as the server sent it, before Normalise.</param>
        /// <param name="serverPlugin">The client plugin GUID the server says its backport belongs to; null when undeclared.</param>
        public static ServedVerdict Judge(ManagedMap? map, string? served, string? serverPlugin)
        {
            var id = Clean(served);
            if (map == null || id.Length == 0 || id == MapVariant.Unmanaged) return ServedVerdict.Unverified;
            if (id == MapVariant.Original) return ServedVerdict.Match;
            if (map.Sources.Any(s => s.Id == id)) return ServedVerdict.Match;

            var backportSide = id == MapVariant.Backport || map.Extras.Any(x => x.Id == id);
            if (!backportSide) return ServedVerdict.UnknownVariant;
            if (!map.HasBackport) return ServedVerdict.NoBackportHere;

            var theirs = Plugin(serverPlugin);
            var ours = Plugin(map.PluginGuid);
            if (theirs == null || ours == null) return ServedVerdict.Unverified;
            return string.Equals(theirs, ours, StringComparison.OrdinalIgnoreCase)
                ? ServedVerdict.Match
                : ServedVerdict.BackportOfAnotherMod;
        }

        /// <summary>True for the verdicts where this client cannot load what the server serves.</summary>
        public static bool IsMismatch(ServedVerdict verdict)
            => verdict == ServedVerdict.UnknownVariant || verdict == ServedVerdict.NoBackportHere
               || verdict == ServedVerdict.BackportOfAnotherMod;

        /// <summary>One log line for a mismatch, naming what is missing; null for any other verdict.</summary>
        public static string? Describe(ManagedMap? map, string locationId, string? served, string? serverPlugin, ServedVerdict verdict)
        {
            var id = Clean(served);
            var theirs = Plugin(serverPlugin);
            var by = theirs == null ? "" : $" (client plugin {theirs})";
            switch (verdict)
            {
                case ServedVerdict.UnknownVariant:
                    return $"the server serves '{id}' for '{locationId}', and no plugin on this client offers that variant. "
                           + $"Install the mod that provides '{id}' on this client too, or that raid loads the wrong scenes over its data.";
                case ServedVerdict.NoBackportHere:
                    return $"the server serves a backport of '{locationId}'{by}, and this client has no backport for that map. "
                           + "Install the same map mods on every client and the server.";
                case ServedVerdict.BackportOfAnotherMod:
                    return $"the server's backport of '{locationId}' belongs to {theirs}, but this client's belongs to "
                           + $"{Plugin(map?.PluginGuid)}. Two mods each claim that map; install the same one on the server "
                           + "and every client.";
                default:
                    return null;
            }
        }

        static string Clean(string? variant) => (variant ?? "").Trim().ToLowerInvariant();

        static string? Plugin(string? guid) => string.IsNullOrWhiteSpace(guid) ? null : guid!.Trim();
    }
}
