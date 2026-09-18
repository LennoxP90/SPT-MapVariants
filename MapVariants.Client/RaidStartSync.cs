using System;
using System.Reflection;
using EFT;
using HarmonyLib;
using MapVariants.Shared;

namespace MapVariants.Client
{
    /// <summary>
    /// The decision point a client with no map screen reaches: a Fika headless, and
    /// a Fika client joining someone else's raid. LocalGameMatching runs outside any
    /// scene load, which is what lets Apply unpatch here (docs/DESIGN-NOTES.md).
    /// </summary>
    // It is TOO LATE here to change what the SERVER serves: the location table is already built, so
    // this only ever makes the client match. It never posts a choice and never overrides one.
    internal static class RaidStartSync
    {
        const string Source = "the raid start";

        // Resolved once and checked, so a game build that moved either member warns rather than throws.
        static readonly MethodInfo Matching = AccessTools.Method(typeof(TarkovApplication), "LocalGameMatching");

        // By reflection, not Harmony's ____raidSettings injection: a missing field makes an injected
        // parameter throw at patch time and takes the whole hook out.
        static readonly FieldInfo RaidSettingsField = AccessTools.Field(typeof(TarkovApplication), "_raidSettings");

        internal static void Install()
        {
            if (Matching == null || RaidSettingsField == null)
            {
                Plugin.Log.LogWarning($"[MV] this game build has no TarkovApplication.{(Matching == null ? "LocalGameMatching" : "_raidSettings")}; "
                                      + "a client with no map screen will load whatever the backport's patches say and the server will be made to agree");
                return;
            }

            new Harmony(BuildInfo.Guid + ".raidstart")
                .Patch(Matching, prefix: new HarmonyMethod(AccessTools.Method(typeof(RaidStartSync), nameof(Before))));
            Plugin.Log.LogInfo("[MV] raid start sync armed");
        }

        // Never returns false: the raid preparer must run exactly as it would with this mod absent.
        static void Before(TarkovApplication __instance, bool inTransition)
        {
            try
            {
                var settings = RaidSettingsField.GetValue(__instance) as RaidSettings;
                var locationId = ManagedMap.Normalise(settings?.LocationId);
                if (locationId.Length == 0) return;

                // Before anything is asked: Ask is a synchronous main-thread request with a retry.
                var map = ClientMaps.Set.For(locationId);
                if (map == null) return;
                if (ClientMaps.ReportIfBackportMissing(map)) return;

                // The map screen and the transit claim are the authority and have already applied.
                if (!RaidStartPolicy.NeedsServerAnswer(locationId, BackportSuspend.SessionLocationId, BackportSuspend.SessionChoice))
                {
                    Plugin.Log.LogDebug($"[MV] {map.Key}: '{BackportSuspend.SessionChoice}' was already chosen for '{locationId}'; the raid start changes nothing");
                    return;
                }

                var answer = VariantSync.Ask(locationId, out var unmanaged);
                if (answer == null)
                {
                    // BackportSuspend.Before still settles it against what the patch state says.
                    Plugin.Log.LogError(unmanaged
                        ? $"[MV] the server does not manage '{locationId}' at raid start: it found no backport there, or discovery failed. "
                          + "Nothing is applied and the load path reports what actually runs."
                        : $"[MV] the server did not answer for '{locationId}' at raid start; nothing is applied and the load path "
                          + "reports what actually runs");
                    return;
                }

                Plugin.Log.LogInfo($"[MV] no choice was made for '{locationId}' on this client{(inTransition ? " (a transit)" : "")}; "
                                   + $"the server says '{answer}'");
                BackportSuspend.SetChoice(locationId, answer);
                BackportSuspend.Apply(locationId, answer, Source);
            }
            catch (Exception e)
            {
                // A throw out of here would abort the game's own raid preparer.
                Plugin.Log.LogError($"[MV] the raid start sync failed, the raid continues on whatever the patch state holds: {e}");
            }
        }
    }
}
