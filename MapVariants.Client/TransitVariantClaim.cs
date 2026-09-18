using System;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.Interactive;
using MapVariants.Shared;
using HarmonyLib;
using UnityEngine;

namespace MapVariants.Client
{
    /// <summary>
    /// A transit has no map screen, so the first player into a managed transit
    /// zone claims a variant for the whole group and everyone who joins reads it
    /// back. Claimed on entering the zone, while the countdown is still running.
    /// </summary>
    internal static class TransitVariantClaim
    {
        static string _claimed;
        static bool _owned;

        // Set on the first claim and cleared only at raid end. A player has several colliders and the
        // transit is inactive for the first minute, so waiting by it re-enters repeatedly.
        static string _claimedFor;

        // Latched separately, because no claim is held for an unmanaged destination and the
        // held-claim guard would let every re-entry post again. See docs/DESIGN-NOTES.md.
        static string _unmanagedFor;

        internal static void Install()
        {
            new Harmony(BuildInfo.Guid + ".transitclaim").PatchAll(typeof(ZoneWatch));
            Plugin.Log.LogInfo("[MV] transit claim armed");
        }

        internal static void Forget()
        {
            _claimed = null;
            _claimedFor = null;
            _unmanagedFor = null;
            _owned = false;
        }

        // Only the claimant is offered a choice: anyone else joined a transit somebody already set,
        // and the server would refuse to change it anyway.
        internal static bool Owns(string destination) => _owned && _claimedFor == destination;

        // Picked from the transit's own interaction menu.
        internal static void Choose(string destination, string variant)
        {
            if (MapVariant.Normalise(_claimed) == MapVariant.Normalise(variant)) return;
            Claim(destination, variant);
        }

        // What this player would pick if asked. A transit has no prompt, so the destination map's
        // configured default is their standing preference.
        static string Preference(string destination)
        {
            var map = ClientMaps.Set.For(destination);
            return map == null ? MapVariant.Original : ClientMaps.DefaultFor(map);
        }

        static bool IsOurs(Collider col)
        {
            var main = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.MainPlayer : null;
            return main != null && col != null && col.GetComponentInParent<Player>() == main;
        }

        static void Claim(string destination, string offered)
        {
            var winner = VariantSync.Claim(destination, offered, out var outcome);
            if (winner == null)
            {
                if (MapVariant.IsUnmanaged(outcome))
                {
                    _unmanagedFor = destination;
                    Plugin.Log.LogError($"[MV] the server does not manage '{destination}'; this transit loads whatever its table serves");
                    return;
                }

                // A failed call claimed nothing, so the state must not say one was claimed. Clearing
                // this is what lets the next zone entry try again.
                _claimedFor = null;
                Plugin.Log.LogWarning($"[MV] transit to '{destination}': nothing claimed; this will be retried on the next zone entry");
                return;
            }

            _claimed = winner;
            _owned = outcome != "Joined";

            // Applied mid-raid because nothing is loading: the countdown is still running and the
            // destination's scenes are minutes away. Its own try so a failure cannot stop the transit.
            try { BackportSuspend.Apply(destination, winner, "a transit claim"); }
            catch (Exception e) { Plugin.Log.LogError($"[MV] transit to '{destination}': applying '{winner}' failed: {e}"); }

            var label = VariantDisplay.For(ClientMaps.Set.For(destination), winner);

            // This only reports what the transit is set to; the choice itself is on the transfer
            // screen that holding the interact key opens.
            var message = outcome == "Joined"
                ? $"Transit is going to {label}"
                : $"Transit set to {label}";
            Notify(message);
            Plugin.Log.LogInfo($"[MV] transit to '{destination}': {outcome} -> {winner}");
        }

        static void Notify(string message)
        {
            try
            {
                NotificationManager.DisplayMessageNotification(message);
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[MV] could not show a notification: {e.GetType().Name}");
            }
        }

        [HarmonyPatch]
        internal static class ZoneWatch
        {
            [HarmonyPatch(typeof(TransitPoint), nameof(TransitPoint.OnTriggerEnter))]
            [HarmonyPostfix]
            static void Entered(TransitPoint __instance, Collider col)
            {
                try
                {
                    if (__instance == null) return;
                    var destination = __instance.parameters.location;
                    var map = ClientMaps.Set.For(destination);
                    if (map == null) return;
                    if (!IsOurs(col)) return;
                    if (ClientMaps.ReportIfBackportMissing(map)) return;

                    // On a claim actually being HELD, not merely attempted: a failed claim used to
                    // record the destination and lock this player out for the rest of the raid.
                    if (_claimedFor == destination && _claimed != null) return;
                    if (_unmanagedFor == destination) return;
                    _claimedFor = destination;
                    _claimed = null;
                    _owned = false;

                    Claim(destination, Preference(destination));
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"[MV] transit claim failed: {e}");
                }
            }

        }
    }
}
