using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT;
using EFT.UI.Matchmaker;
using MapVariants.Shared;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace MapVariants.Client
{
    /// <summary>
    /// Asks which variant to load before the map screen advances. It patches the
    /// destination, the screen controller's ShowNextScreen and ShowReadyScreen,
    /// rather than rewiring the buttons, which two mods cannot both do.
    /// </summary>
    [HarmonyPatch]
    internal static class MapVariantPrompt
    {
        static bool _controllerPatched;

        // Captured at patch time. Asking Harmony for MethodBase __originalMethod makes it emit a
        // GetMethodFromHandle call, which THROWS for an open generic declaring type, so every click
        // threw inside the wrapper. See docs/DESIGN-NOTES.md.
        static MethodInfo _next, _ready;

        // Mono shares native code across reference-type instantiations, so patching the generic base
        // patches every screen in the sequence. Without this filter the prompt reappears on each.
        static Type _controllerType;

        // Set just before the original is re-invoked with the answer, so the prefix lets that one
        // call through instead of prompting again.
        static bool _passThrough;

        internal static void Install()
        {
            new Harmony(BuildInfo.Guid + ".prompt").PatchAll(typeof(MapVariantPrompt));
            Plugin.Log.LogInfo("[MV] map prompt armed");
        }

        // A postfix on Awake only to get hold of the controller: its type is nested and cannot be
        // resolved by name.
        [HarmonyPatch(typeof(MatchMakerSelectionLocationScreen), nameof(MatchMakerSelectionLocationScreen.Awake))]
        [HarmonyPostfix]
        static void PatchController(MatchMakerSelectionLocationScreen __instance)
        {
            try
            {
                if (_controllerPatched) return;

                var controller = Controller(__instance);
                if (controller == null) { Plugin.Log.LogWarning("[MV] no screen controller on the map screen; no prompt"); return; }

                _controllerType = controller.GetType();

                var harmony = new Harmony(BuildInfo.Guid + ".screen");
                _next = Declared(controller.GetType(), "ShowNextScreen");
                _ready = Declared(controller.GetType(), "ShowReadyScreen");

                var patched = 0;
                if (_next != null) { harmony.Patch(_next, new HarmonyMethod(AccessTools.Method(typeof(MapVariantPrompt), nameof(BeforeNext)))); patched++; }
                if (_ready != null) { harmony.Patch(_ready, new HarmonyMethod(AccessTools.Method(typeof(MapVariantPrompt), nameof(BeforeReady)))); patched++; }

                _controllerPatched = patched > 0;
                Plugin.Log.LogDebug($"[MV] hooked {patched} advance method(s); ShowNextScreen declared on " +
                                    $"'{_next?.DeclaringType?.FullName ?? "not found"}'");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[MV] could not hook the screen controller; no prompt: {e}");
            }
        }

        // Walks up from the concrete controller so the method comes off the CLOSED constructed base
        // type rather than the open generic definition.
        static MethodInfo Declared(Type type, string name)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                var found = current.GetMethod(name, BindingFlags.Instance | BindingFlags.Public
                                                  | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (found != null) return found;
            }
            Plugin.Log.LogWarning($"[MV] {type.Name}.{name} not found on any base type");
            return null;
        }

        // One prefix per button, so neither needs __originalMethod to know which it is.
        static bool BeforeNext(object __instance) => BeforeAdvance(__instance, _next, "ShowNextScreen");

        static bool BeforeReady(object __instance) => BeforeAdvance(__instance, _ready, "ShowReadyScreen");

        // Returning false stops the screen advancing until the player answers.
        static bool BeforeAdvance(object __instance, MethodInfo original, string name)
        {
            try
            {
                if (_passThrough) { _passThrough = false; return true; }

                // Any other screen in the sequence shares this patched method; it is not ours.
                if (__instance == null || __instance.GetType() != _controllerType) return true;

                var settings = AccessTools.Field(__instance.GetType(), "RaidSettings")?.GetValue(__instance) as RaidSettings;
                var locationId = settings?.SelectedLocation?.Id ?? "";

                var map = ClientMaps.Set.For(locationId);
                if (map != null || locationId.Length == 0)
                    Plugin.Log.LogDebug($"[MV] {name}: location '{(locationId.Length == 0 ? "none" : locationId)}', managed={map != null}, ask={ClientMaps.Prompt}");
                if (map == null) return true;
                if (ClientMaps.ReportIfBackportMissing(map)) return true;

                Action proceed = () =>
                {
                    _passThrough = true;
                    if (original == null) { _passThrough = false; return; }
                    try { original.Invoke(__instance, null); }
                    catch (Exception e) { _passThrough = false; Plugin.Log.LogError($"[MV] advancing the screen failed: {e}"); }
                };

                if (!ClientMaps.Prompt)
                {
                    Choose(ClientMaps.DefaultFor(map), locationId, map, proceed, "config");
                    return false;
                }

                Ask(map, locationId, proceed);
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[MV] prefix failed, the screen advances unprompted: {e}");
                return true;
            }
        }

        static void Ask(ManagedMap map, string locationId, Action proceed)
        {
            var preview = FindPreview(map.Key);
            MapChoiceWindow.Show(
                "CHOOSE YOUR " + map.DisplayName.ToUpperInvariant(),
                TileImage(map.OriginalTile) ?? preview, VariantDisplay.For(map, MapVariant.Original),
                TileImage(map.BackportTile) ?? preview, VariantDisplay.For(map, MapVariant.Backport),
                Warnings(map),
                choice =>
                {
                    if (choice == null) { Plugin.Log.LogDebug("[MV] cancelled; staying on the map screen"); return; }
                    Choose(choice.Value ? MapVariant.Backport : MapVariant.Original, locationId, map, proceed, "player");
                });
        }

        // Its own try: a throwing supplier costs the warning line, never the player's ability to
        // pick a map.
        internal static string[] Warnings(ManagedMap map)
        {
            var supplier = ClientMaps.BackportWarnings(map);
            if (supplier == null) return new string[0];
            try
            {
                return WarningPolicy.Normalise(supplier());
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[MV] {map.Key}: its warning supplier threw, the window shows no warning line: {e}");
                return new string[0];
            }
        }

        static void Choose(string variant, string locationId, ManagedMap map, Action proceed, string source)
        {
            try
            {
                // Post first, record second, and record only what came back: a choice the server
                // never stored has this client loading scenes the host is not serving data for.
                var recorded = VariantSync.Post(locationId, variant);
                if (recorded == null)
                {
                    Plugin.Log.LogError($"[MV] the server did not acknowledge '{variant}' for '{locationId}'; "
                                        + "no choice recorded, this raid loads whatever the table is serving");
                }
                else
                {
                    BackportSuspend.SetChoice(locationId, recorded);
                    Plugin.Log.LogInfo($"[MV] {source} chose {VariantDisplay.For(map, recorded)}");

                    // Here and not on the load path: the raid is not even created yet.
                    BackportSuspend.Apply(locationId, recorded, "the map screen");
                }
            }
            catch (Exception e)
            {
                // The window is already gone, so a throw here would leave the player unable to start
                // the raid at all.
                Plugin.Log.LogError($"[MV] recording the choice failed, the raid continues on whatever the server serves: {e}");
            }
            proceed();
        }

        static object Controller(MatchMakerSelectionLocationScreen screen)
            => AccessTools.Field(screen.GetType(), "ScreenController")?.GetValue(screen)
            ?? AccessTools.Property(screen.GetType(), "ScreenController")?.GetValue(screen);

        // One screenshot per variant from tiles/; a missing file falls back to the game's own preview.
        static readonly Dictionary<string, Sprite> Tiles = new Dictionary<string, Sprite>();

        internal static Sprite TileImage(string file)
        {
            if (file == null) return null;
            if (Tiles.TryGetValue(file, out var cached)) return cached;

            Sprite sprite = null;
            var path = System.IO.Path.Combine(Plugin.PluginDir, file.Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(path))
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (texture.LoadImage(System.IO.File.ReadAllBytes(path)))
                {
                    texture.hideFlags = HideFlags.HideAndDontSave | HideFlags.DontUnloadUnusedAsset;
                    sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                    sprite.hideFlags = HideFlags.HideAndDontSave | HideFlags.DontUnloadUnusedAsset;
                    Plugin.Log.LogDebug($"[MV] tile image {file} {texture.width}x{texture.height}");
                }
                else Plugin.Log.LogWarning($"[MV] {file} did not decode as an image");
            }

            Tiles[file] = sprite;
            return sprite;
        }

        static readonly HashSet<string> _previewReported = new HashSet<string>();

        // The largest loaded sprite whose name mentions the map key. Searched globally because the
        // prefix runs from the controller and has no screen reference.
        internal static Sprite FindPreview(string key)
        {
            var best = Resources.FindObjectsOfTypeAll<Sprite>()
                .Where(s => s != null && (s.name ?? "").IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderByDescending(s => s.rect.width * s.rect.height)
                .FirstOrDefault();

            if (_previewReported.Add(key))
            {
                Plugin.Log.LogDebug(best == null
                    ? $"[MV] no '{key}' preview sprite found; the tiles show a flat colour"
                    : $"[MV] preview sprite '{best.name}' {best.rect.width}x{best.rect.height}");
            }

            return best;
        }
    }
}
