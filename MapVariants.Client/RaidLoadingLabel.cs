using System;
using System.Linq;
using System.Reflection;
using EFT.UI.Matchmaker;
using MapVariants.Shared;
using HarmonyLib;

namespace MapVariants.Client
{
    /// <summary>
    /// With the prompt turned off there is no other moment that tells a player
    /// which variant is loading, so the last screen before the raid says so.
    /// </summary>
    internal static class RaidLoadingLabel
    {
        internal static void Install()
        {
            // Show is overloaded; the three-argument one carries the RaidSettings we need.
            var target = typeof(MatchmakerTimeHasCome)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == nameof(MatchmakerTimeHasCome.Show) && m.GetParameters().Length == 3);

            if (target == null)
            {
                Plugin.Log.LogWarning("[MV] MatchmakerTimeHasCome.Show(session, settings, matchmaker) not found; the loading screen stays unlabelled");
                return;
            }

            new Harmony(BuildInfo.Guid + ".loadinglabel")
                .Patch(target, postfix: new HarmonyMethod(AccessTools.Method(typeof(RaidLoadingLabel), nameof(AfterShow))));
            Plugin.Log.LogInfo("[MV] loading label armed");
        }

        // A postfix: the game writes _locationName inside Show, so a prefix would be overwritten.
        static void AfterShow(MatchmakerTimeHasCome __instance, object[] __args)
        {
            try
            {
                var label = __instance._locationName;
                if (label == null) return;

                var settings = __args.OfType<EFT.RaidSettings>().FirstOrDefault();
                var locationId = settings?.SelectedLocation?.Id ?? "";
                var map = ClientMaps.Set.For(locationId);
                if (map == null) return;

                // The final fallback is the backport, matching the load path. This client's configured
                // default labelled every transit with the player's preference rather than with what
                // was actually loading.
                var variant = BackportSuspend.SessionChoice ?? VariantSync.Ask(locationId, out _) ?? MapVariant.Backport;
                var suffix = Suffix(VariantDisplay.For(map, variant));
                var other = Suffix(VariantDisplay.For(map, MapVariant.IsBackport(variant) ? MapVariant.Original : MapVariant.Backport));

                var text = label.text ?? "";
                // Both suffixes are checked, so a second Show cannot stack one on top of the other.
                if (text.EndsWith(suffix, StringComparison.Ordinal) || text.EndsWith(other, StringComparison.Ordinal)) return;

                label.text = text + suffix;
                Plugin.Log.LogDebug($"[MV] loading screen marked '{label.text}'");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[MV] {e.GetType().Name}: {e.Message}");
            }
        }

        // "Lighthouse - Vanilla" becomes " - Vanilla": the game has already written the map's name.
        static string Suffix(string label)
        {
            var dash = label.IndexOf(" - ", StringComparison.Ordinal);
            return dash < 0 ? " - " + label : label.Substring(dash);
        }
    }
}
