using System;
using MapVariants.Shared;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;

namespace MapVariants.Client
{
    /// <summary>
    /// Tells the server which variant the player picked, and asks what a host
    /// picked. RequestHandler rather than HttpClient, because SPT inflates request
    /// bodies with no header to say so and a hand-rolled POST dies in its inflater.
    /// </summary>
    internal static class VariantSync
    {
        const string ChoiceRoute = "/mapvariants/variant";

        // A separate path for reads: a route declaring a body type fails on the null body SPT hands
        // every GET.
        const string HostRoute = "/mapvariants/hostvariant";

        const string ClaimRoute = "/mapvariants/transitclaim";

        // Returns what the server says it RECORDED, null otherwise. The client suspends a
        // backport's patches on the strength of it, so a choice it never stored is not acted on.
        internal static string Post(string locationId, string variant)
        {
            try
            {
                var body = new JObject { ["locationId"] = locationId, ["variant"] = variant }.ToString();
                var json = RequestHandler.PostJson(ChoiceRoute, body);
                if (string.IsNullOrWhiteSpace(json)) return null;

                var recorded = JObject.Parse(json)["variant"]?.ToString();
                if (string.IsNullOrWhiteSpace(recorded)) return null;

                if (MapVariant.IsUnmanaged(recorded))
                {
                    Plugin.Log.LogError($"[MV] the server does not manage '{locationId}'; it did not record '{variant}'");
                    return null;
                }

                Plugin.Log.LogDebug($"[MV] told the server '{variant}' for '{locationId}'; it recorded '{recorded}'");
                return MapVariant.Normalise(recorded);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[MV] could not post '{variant}': {e.GetType().Name}: {e.Message}");
                return null;
            }
        }

        // First caller wins, so the answer is where the group is going, not necessarily what was
        // offered.
        internal static string Claim(string locationId, string variant, out string outcome)
        {
            outcome = null;
            try
            {
                var body = new JObject { ["locationId"] = locationId, ["variant"] = variant }.ToString();
                var json = RequestHandler.PostJson(ClaimRoute, body);
                if (string.IsNullOrWhiteSpace(json)) return null;

                var answer = JObject.Parse(json);
                var winner = answer["variant"]?.ToString();
                outcome = answer["outcome"]?.ToString();
                if (string.IsNullOrWhiteSpace(winner)) return null;

                // Carried in the outcome rather than normalised, so the caller can tell a definitive
                // answer from a failed call and not retry it.
                if (MapVariant.IsUnmanaged(winner))
                {
                    outcome = MapVariant.Unmanaged;
                    return null;
                }

                return MapVariant.Normalise(winner);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[MV] could not claim '{variant}' for '{locationId}': {e.GetType().Name}: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// What the table is serving right now, for a caller that answered no
        /// prompt. Null is no usable answer; unmanaged is set only when the server
        /// said so, which is a different thing and must not read as original.
        /// </summary>
        internal static string Ask(string locationId, out bool unmanaged)
        {
            unmanaged = false;

            // One retry: the answer decides whether a backport's patches are suspended, so a single
            // dropped request should not cost this raid its answer.
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    var json = RequestHandler.GetJson($"{HostRoute}?locationId={locationId}");
                    if (string.IsNullOrWhiteSpace(json)) continue;

                    var variant = JObject.Parse(json)["variant"]?.ToString();
                    if (string.IsNullOrWhiteSpace(variant)) continue;

                    if (MapVariant.IsUnmanaged(variant))
                    {
                        unmanaged = true;
                        return null;
                    }

                    Plugin.Log.LogDebug($"[MV] the server says '{variant}' for '{locationId}'");
                    return MapVariant.Normalise(variant);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"[MV] could not read the host's choice (attempt {attempt} of 2): {e.GetType().Name}: {e.Message}");
                }
            }

            return null;
        }
    }
}
