using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;

namespace MapVariants.Client
{
    // Soft, not hard: a missing backport must cost that one map its choice, not load the whole mod.
    // They only order the load; presence is decided at use, so a third backport added through
    // maps.json alone needs no attribute here. See docs/DESIGN-NOTES.md.
    [BepInDependency("com.manimal.lighthouse", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.manimal.interchange", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInPlugin(BuildInfo.Guid, "MapVariants", BuildInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static string PluginDir;

        // Everything arms here and nothing in Start: with HideManagerGameObject at its stock BepInEx
        // default the manager object is torn down before end of frame and Start never runs at all.
        void Awake()
        {
            Log = Logger;
            PluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            Safe(nameof(ClientMaps), () => ClientMaps.Install(Config, PluginDir));
            Safe(nameof(ConfigReload), () => ConfigReload.Install(Config));
            Safe(nameof(RaidLifecycle), RaidLifecycle.Install);
            Safe(nameof(MapVariantPrompt), MapVariantPrompt.Install);
            Safe(nameof(RaidLoadingLabel), RaidLoadingLabel.Install);
            Safe(nameof(TransitVariantClaim), TransitVariantClaim.Install);
            Safe(nameof(TransitChoicePrompt), TransitChoicePrompt.Install);
            Safe(nameof(BackportSuspend), BackportSuspend.Install);
            // After the suspend, because it needs ClientMaps bound before it can resolve a location.
            Safe(nameof(RaidStartSync), RaidStartSync.Install);
            Safe("startup log", () => Log.LogInfo($"[MV] MapVariants {BuildInfo.Version} loaded; prompt={ClientMaps.Prompt}; {ClientMaps.Summary()}"));
            Safe("ApiSelfCheck", Api.ApiSelfCheck.Run);
        }

        internal static void Safe(string name, Action install)
        {
            try { install(); }
            catch (Exception e) { Log.LogError($"[MV] {name} install failed, continuing without it: {e}"); }
        }
    }
}
