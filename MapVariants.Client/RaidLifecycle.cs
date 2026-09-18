using System;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

namespace MapVariants.Client
{
    /// <summary>
    /// Clears the raid's answer once that raid has fully unloaded, so the next one
    /// decides for itself. A Fika headless serves many raids in one process and
    /// never sees a map screen, so this is its only reset.
    /// </summary>
    // Map-agnostic on purpose: a backport's scenes are not necessarily named after its location id,
    // so the raid's scene set is learned by watching what loads while a choice is live.
    internal static class RaidLifecycle
    {
        static readonly HashSet<string> LoadedWhileLive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal static void Install()
        {
            SceneManager.sceneLoaded += (scene, mode) => OnLoaded(scene);
            SceneManager.sceneUnloaded += OnUnloaded;
            Plugin.Log.LogInfo("[MV] lifecycle armed");
        }

        static void OnLoaded(Scene scene)
        {
            try
            {
                if (BackportSuspend.SessionLocationId == null) return;
                var name = scene.name;
                if (string.IsNullOrEmpty(name)) return;
                LoadedWhileLive.Add(name);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[MV] lifecycle sceneLoaded handler threw, continuing: {e}");
            }
        }

        static void OnUnloaded(Scene scene)
        {
            try
            {
                var name = scene.name;
                if (string.IsNullOrEmpty(name) || !LoadedWhileLive.Contains(name)) return;

                LoadedWhileLive.Clear();
                BackportSuspend.ForgetChoice();
                TransitVariantClaim.Forget();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[MV] lifecycle sceneUnloaded handler threw, continuing: {e}");
            }
        }
    }
}
