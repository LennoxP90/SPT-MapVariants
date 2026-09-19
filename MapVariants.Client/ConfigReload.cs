using System;
using System.IO;
using BepInEx.Configuration;
using UnityEngine;

namespace MapVariants.Client
{
    /// <summary>
    /// BepInEx reads the cfg once at start; this watches the file so an edit
    /// between raids applies without a relaunch. Every value is read through
    /// ConfigEntry.Value at the point of use, and one cached value undoes that.
    /// </summary>
    internal sealed class ConfigReload : MonoBehaviour
    {
        static ConfigFile _config;
        static FileSystemWatcher _watcher;
        static volatile bool _dirty;

        // Its own GameObject: the plugin's own gets no reliable Update, for the same reason nothing
        // here may run from Start. See docs/DESIGN-NOTES.md.
        internal static void Install(ConfigFile config)
        {
            var host = new GameObject("MV_ConfigReload") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(host);
            _config = config;
            var path = config.ConfigFilePath;
            _watcher = new FileSystemWatcher(Path.GetDirectoryName(path), Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += (_, __) => _dirty = true;
            host.AddComponent<ConfigReload>();
        }

        void Update()
        {
            if (!_dirty) return;
            _dirty = false;
            try
            {
                _config.Reload();
                Plugin.Log.LogInfo("[MV] config reloaded");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[MV] config reload failed: {e.Message}");
            }
        }
    }
}
