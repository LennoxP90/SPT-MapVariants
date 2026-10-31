using System;
using System.Reflection;
using EFT;
using MapVariants.Shared;
using HarmonyLib;

namespace MapVariants.Client
{
    /// <summary>
    /// The variant choice, on the item-transfer screen the transit already opens:
    /// the only interactive UI a raid has, and closing it is what commits the
    /// transit. Shown only to the player holding the claim.
    /// </summary>
    internal static class TransitChoicePrompt
    {
        internal static void Install()
        {
            new Harmony(BuildInfo.Guid + ".transitprompt").PatchAll(typeof(OnScreen));
            Plugin.Log.LogInfo("[MV] transit prompt armed");
        }

        [HarmonyPatch]
        internal static class OnScreen
        {
            static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(ClientTransitController), nameof(ClientTransitController.OpenTransferItemsScreen));

            static void Postfix(ClientTransitController __instance, int pointId)
            {
                try
                {
                    if (__instance == null || !__instance.pointsById.TryGetValue(pointId, out var point) || point == null)
                        return;

                    // Keyed on THIS transit's destination, never on the remembered claim: the claim
                    // survives the raid it was made for, so it would prompt on the way out and, with
                    // several managed maps, for the wrong one.
                    var destination = point.parameters.location;

                    var map = ClientMaps.Set.For(destination);
                    if (map == null) return;
                    if (ClientMaps.ReportIfBackportMissing(map)) return;
                    if (!TransitVariantClaim.Owns(destination))
                    {
                        Plugin.Log.LogDebug($"[MV] transit to '{destination}' was claimed by someone else; no prompt");
                        return;
                    }
                    MapVariantPrompt.ShowChoice(map, choice =>
                    {
                        // Cancelled: keep what entering the zone already claimed.
                        if (choice == null) return;
                        TransitVariantClaim.Choose(destination, choice);
                    });
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"[MV] transit prompt failed: {e}");
                }
            }
        }
    }
}
