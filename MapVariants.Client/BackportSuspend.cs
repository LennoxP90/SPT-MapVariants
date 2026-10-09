using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using EFT;
using HarmonyLib;
using MapVariants.Shared;
using UnityEngine.SceneManagement;

namespace MapVariants.Client
{
    /// <summary>
    /// Makes the client match the variant chosen for a raid: a shipped raid
    /// removes the backport's patches on LoadPresetAsync, a backport raid puts
    /// them back, and a registered provider is told to do its own swap.
    /// </summary>
    // Apply runs from the map screen and from a transit claim, both outside any scene load, so
    // Harmony's rebuild lands on the next LoadPresetAsync. Before only observes. Changing the patch
    // set from inside that load crashed two raids; see docs/DESIGN-NOTES.md.
    internal static class BackportSuspend
    {
        static readonly MethodInfo LoadPreset =
            AccessTools.Method(typeof(LoadScenesFromPresetOperation), nameof(LoadScenesFromPresetOperation.LoadPresetAsync));
        static readonly Harmony Ours = new Harmony(BuildInfo.Guid + ".suspend");

        sealed class Live
        {
            public HarmonyPatchType Kind;
            public MethodInfo Method;
            public string Owner;
            public int Priority;
            public string[] Before, After;
        }

        // Per map key: what is currently unpatched. Empty means the backport's patches are live.
        static readonly Dictionary<string, List<Live>> Suspended = new Dictionary<string, List<Live>>();

        static readonly HashSet<string> Reported = new HashSet<string>();

        // ChildPresets of a preset already loading. The root's ServerName is the raid's location;
        // a child's can name another (Factory night's culling child says factory4_day).
        static readonly HashSet<ScenesPreset> Children = new HashSet<ScenesPreset>();

        // Per map key: whether this process has ever run the BACKPORT, so the next shipped raid knows
        // there may be state to stand down. Cleared when a stand-down succeeds.
        static readonly HashSet<string> _backportLoadedThisSession = new HashSet<string>();

        static string _sessionChoice, _sessionLocationId, _announced;
        static bool _asked;
        internal static event Action<string> Resolved;

        internal static string SessionLocationId => _sessionLocationId;

        internal static string SessionChoice
        {
            get => _sessionChoice;
            set
            {
                // Normalised on assignment: a caller storing a display label would make every
                // comparison against Original/Backport quietly false.
                _sessionChoice = value == null ? null : MapVariant.Normalise(value);
                if (_sessionChoice == null) { _announced = null; return; }
                if (_sessionChoice == _announced) return;
                _announced = _sessionChoice;
                Announce(_sessionChoice);
            }
        }

        internal static void SetChoice(string locationId, string variant)
        {
            _sessionLocationId = ManagedMap.Normalise(locationId);
            SessionChoice = variant;
        }

        internal static void ForgetChoice()
        {
            SessionChoice = null;
            _sessionLocationId = null;
            _asked = false;
        }

        static void Announce(string variant)
        {
            var handlers = Resolved;
            if (handlers == null) return;
            foreach (var handler in handlers.GetInvocationList())
            {
                try { ((Action<string>)handler)(variant); }
                catch (Exception e)
                {
                    var owner = handler.Target?.GetType().FullName ?? handler.Method.DeclaringType?.FullName ?? "an unknown subscriber";
                    Plugin.Log.LogError($"[MV] a VariantResolved handler in {owner} threw, continuing: {e}");
                }
            }
        }

        // The prefix and nothing else. Anything that enumerates another plugin's patches waits for
        // Apply instead, because this plugin's Start() never runs (docs/DESIGN-NOTES.md).
        internal static void Install()
        {
            Ours.Patch(LoadPreset, prefix: new HarmonyMethod(AccessTools.Method(typeof(BackportSuspend), nameof(Before))) { priority = Priority.First });
            Plugin.Log.LogInfo("[MV] suspend armed");
        }

        static Assembly PluginAssembly(ManagedMap map)
            => map.PluginGuid != null && Chainloader.PluginInfos.TryGetValue(map.PluginGuid, out var info) && info.Instance != null
                ? info.Instance.GetType().Assembly : null;

        static string AssemblyName(ManagedMap map) => PluginAssembly(map)?.GetName().Name ?? "?";

        // One selection serves both the diagnostic and the act, so the tested policy decides what
        // actually gets unpatched.
        static List<Live> Selected(ManagedMap map)
        {
            var ours = typeof(BackportSuspend).Assembly.GetName().Name;
            var pairs = Enumerate()
                .Select(l => (Live: l, Recorded: new RecordedPatch(
                    l.Kind.ToString(), l.Owner,
                    l.Method.DeclaringType?.Assembly.GetName().Name ?? "",
                    l.Method.Name, l.Priority)))
                .ToList();

            var approved = SuspendPolicy.Select(pairs.Select(p => p.Recorded), AssemblyName(map), ours);

            // Reference identity, so two patches sharing an owner id cannot be confused.
            var keep = new HashSet<RecordedPatch>(approved);
            return pairs.Where(p => keep.Contains(p.Recorded)).Select(p => p.Live).ToList();
        }

        static bool _manipulatorsReported;

        static List<Live> Enumerate()
        {
            var result = new List<Live>();
            var info = Harmony.GetPatchInfo(LoadPreset);
            if (info == null) return result;
            Take(result, info.Prefixes, HarmonyPatchType.Prefix);
            Take(result, info.Postfixes, HarmonyPatchType.Postfix);
            Take(result, info.Transpilers, HarmonyPatchType.Transpiler);
            Take(result, info.Finalizers, HarmonyPatchType.Finalizer);

            try { TakeManipulators(result, info); }
            catch (Exception e)
            {
                if (!_manipulatorsReported)
                {
                    _manipulatorsReported = true;
                    Plugin.Log.LogWarning($"[MV] this Harmony does not expose ILManipulators on {LoadPreset.DeclaringType?.Name}.{LoadPreset.Name}; "
                                          + $"an IL manipulator on it would be neither suspended nor restored: {e.GetType().Name}: {e.Message}");
                }
            }
            return result;
        }

        static void Take(List<Live> result, IEnumerable<Patch> patches, HarmonyPatchType kind)
        {
            foreach (var p in patches)
                result.Add(new Live { Kind = kind, Method = p.PatchMethod, Owner = p.owner, Priority = p.priority, Before = p.before, After = p.after });
        }

        // Its own method because ILManipulators exists only on HarmonyX 2.9 and up, and an older
        // Harmony fails when the method READING it is jitted. Only a call from inside a try can catch
        // that, so do not inline either of these two.
        static void TakeManipulators(List<Live> result, Patches info)
            => Take(result, info.ILManipulators, HarmonyPatchType.ILManipulator);

        static void PatchManipulator(Harmony harmony, HarmonyMethod method)
            => harmony.Patch(LoadPreset, ilmanipulator: method);

        static void ReportSuspendSetOnce(ManagedMap map)
        {
            if (!Reported.Add(map.Key)) return;

            // Neither kind of provider: nothing on this client can switch it.
            if (map.PluginGuid == null)
            {
                if (ClientMaps.Callback(map) == null)
                    Plugin.Log.LogWarning($"[MV] {map.Key}: managed with neither a provider callback nor a plugin GUID; "
                                          + "nothing on this client can switch it, and every raid there runs whatever the server serves");
                return;
            }

            var set = Selected(map);
            Plugin.Log.LogInfo($"[MV] {map.Key}: suspend set = {set.Count} patch(es) from {AssemblyName(map)}: "
                               + string.Join(", ", set.Select(l => $"{l.Kind} {l.Owner}:{l.Method.Name} p{l.Priority}")));
            if (set.Count == 0)
                Plugin.Log.LogWarning($"[MV] {map.Key}: {AssemblyName(map)} has no patch on LoadPresetAsync at first sight of this map. "
                                      + "If it never swaps scenes, a stock raid needs nothing suspended; if it swaps them some other way, "
                                      + "nothing here can stop it");
        }

        // Each entry joins the record the instant its own Unpatch succeeds, so a partial failure
        // leaves a record the next raid converges on rather than one Harmony disagrees with.
        static void Suspend(ManagedMap map)
        {
            var taken = Selected(map);
            if (!Suspended.TryGetValue(map.Key, out var record)) Suspended[map.Key] = record = new List<Live>();
            foreach (var l in taken)
            {
                if (record.Any(r => r.Method == l.Method && r.Kind == l.Kind)) continue;
                try
                {
                    Ours.Unpatch(LoadPreset, l.Method);
                    record.Add(l);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"[MV] {map.Key}: could not suspend {l.Kind} {l.Owner}:{l.Method.Name}, it stays live: {e}");
                }
            }
        }

        static void Restore(ManagedMap map)
        {
            if (!Suspended.TryGetValue(map.Key, out var list)) return;
            for (var i = list.Count - 1; i >= 0; i--)
            {
                var l = list[i];
                try
                {
                    var hm = new HarmonyMethod(l.Method) { priority = l.Priority, before = l.Before, after = l.After };
                    var h = new Harmony(l.Owner);
                    switch (l.Kind)
                    {
                        case HarmonyPatchType.Prefix: h.Patch(LoadPreset, prefix: hm); break;
                        case HarmonyPatchType.Postfix: h.Patch(LoadPreset, postfix: hm); break;
                        case HarmonyPatchType.Transpiler: h.Patch(LoadPreset, transpiler: hm); break;
                        case HarmonyPatchType.Finalizer: h.Patch(LoadPreset, finalizer: hm); break;
                        case HarmonyPatchType.ILManipulator: PatchManipulator(h, hm); break;
                    }
                    list.RemoveAt(i);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"[MV] {map.Key}: could not restore {l.Kind} {l.Owner}:{l.Method.Name}, it stays suspended: {e}");
                }
            }
            if (list.Count == 0) Suspended.Remove(map.Key);
        }

        static int SuspendedCount(ManagedMap map) => Suspended.TryGetValue(map.Key, out var l) ? l.Count : 0;

        static string Name(string variant) => MapVariant.IsBackport(variant) ? "BACKPORT" : "ORIGINAL";

        static void NotifyProvider(ManagedMap map, Action<string> callback, string variant)
        {
            try { callback(variant); }
            catch (Exception e) { Plugin.Log.LogError($"[MV] {map.Key}: provider callback threw: {e}"); }
        }

        /// <summary>
        /// Applies the patch state for a variant, from the map screen or a transit
        /// claim, both of which run while nothing is loading. source names the
        /// caller in the log. Nothing here throws back at a caller.
        /// </summary>
        internal static void Apply(string locationId, string variant, string source = "a choice")
        {
            try
            {
                var id = ManagedMap.Normalise(locationId);
                var map = ClientMaps.Set.For(id);
                if (map == null) return;
                if (ClientMaps.ReportIfBackportMissing(map)) return;

                // Reported here, before anything is suspended. From the load path it would report an
                // already-emptied set and warn that the backport had no patch at all.
                ReportSuspendSetOnce(map);

                var wanted = MapVariant.Normalise(variant);
                var wantStock = !MapVariant.IsBackport(wanted);

                // The only way a log shows whether the previous raid's scenes were still loaded.
                Plugin.Log.LogDebug($"[MV] {map.Key}: {SceneManager.sceneCount} scene(s) loaded before applying {wanted}");

                var callback = ClientMaps.Callback(map);
                if (callback != null)
                {
                    // A provider swaps itself and never has its patches touched, so it gets no
                    // stand-down either: its own callback tells it both directions.
                    NotifyProvider(map, callback, wanted);
                    Plugin.Log.LogInfo($"[MV] {map.Key}: applied {Name(wanted)} from {source}; provider notified");
                    return;
                }

                wantStock = StandDownBeforeStock(map, id, wantStock);

                // The RECORD says what moved: a boolean "is this map suspended" cannot see a
                // partial restore.
                var recordedBefore = SuspendedCount(map);
                if (wantStock) Suspend(map); else Restore(map);

                wantStock = WithdrawVetoIfStillLive(map, id, wantStock);

                // On every path that ends on the backport, the two withdrawals included.
                if (!wantStock) _backportLoadedThisSession.Add(map.Key);

                var changed = SuspendPolicy.PatchSetChanged(recordedBefore, SuspendedCount(map));
                Plugin.Log.LogInfo($"[MV] {map.Key}: applied {(wantStock ? "ORIGINAL" : "BACKPORT")} from {source}; "
                                   + $"{(wantStock ? $"suspended {SuspendedCount(map)}" : "restored")}{(changed ? "" : " (unchanged)")}");
                Plugin.Log.LogDebug($"[MV] {map.Key}: {SceneManager.sceneCount} scene(s) loaded after applying");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[MV] could not apply '{variant}' for '{locationId}' from {source}; "
                                    + $"the patch state is unchanged and the load path will report what actually runs: {e}");
            }
        }

        // Returns whether the shipped map is still wanted: a backport that cannot be stood down
        // keeps this raid.
        static bool StandDownBeforeStock(ManagedMap map, string id, bool wantStock)
        {
            // Only here: the previous raid's scenes have unloaded and the next raid's have not, which
            // is the condition a backport's own release path waits for.
            var decision = StandDownPolicy.Decide(wantStock, _backportLoadedThisSession.Contains(map.Key), map.Declaration);
            if (decision == StandDownAction.StandDown)
            {
                var stoodDown = TryStandDown(map);
                decision = StandDownPolicy.AfterAttempt(stoodDown);
                if (stoodDown) _backportLoadedThisSession.Remove(map.Key);
            }

            if (decision != StandDownAction.Refuse) return wantStock;

            var why = map.StandDown == null
                ? "exposes no way to stand it down"
                : $"could not be stood down by {map.StandDown}";
            Plugin.Log.LogError($"[MV] {map.Key}: {AssemblyName(map)} has already loaded this session and {why}, "
                                + "so the shipped map cannot be produced safely. This raid runs the backport. "
                                + "Restart the game to pick the shipped map.");
            SetChoice(id, MapVariant.Backport);
            if (VariantSync.Post(id, MapVariant.Backport) == null)
                Plugin.Log.LogError($"[MV] {map.Key}: the server did not acknowledge the backport; it may still be serving the shipped data");
            return false;
        }

        // Any of the backport's patches left live means it would load its own scenes over the
        // shipped data, so the raid gives the backport back rather than mixing the two.
        static bool WithdrawVetoIfStillLive(ManagedMap map, string id, bool wantStock)
        {
            var stillLive = wantStock ? Selected(map).Count : 0;
            if (!SuspendPolicy.MustWithdrawVeto(wantStock, stillLive)) return wantStock;

            Plugin.Log.LogError($"[MV] {map.Key}: could not remove all of {AssemblyName(map)}'s patches on LoadPresetAsync; "
                                + $"{stillLive} still live, so it would load its own scenes over the shipped data. "
                                + "Withdrawing the veto: this raid runs the BACKPORT.");
            SetChoice(id, MapVariant.Backport);
            if (VariantSync.Post(id, MapVariant.Backport) == null)
                Plugin.Log.LogError($"[MV] {map.Key}: the server did not acknowledge the withdrawal; it may still be serving the shipped data");

            // Put back whatever the half-done suspension did remove, so the backport runs with the
            // patch set it registered rather than a subset of it.
            Restore(map);
            return false;
        }

        // The one thing invoked is the static parameterless method maps.json names, in the
        // backport's own assembly. No field of its is read or written.
        static bool TryStandDown(ManagedMap map)
        {
            var spec = map.StandDown;
            try
            {
                var target = StandDownTarget.Parse(spec);
                if (target == null)
                {
                    Plugin.Log.LogWarning($"[MV] {map.Key}: standDown '{spec}' is not 'Namespace.Type:MethodName', so nothing was called");
                    return false;
                }

                var assembly = PluginAssembly(map);
                if (assembly == null)
                {
                    Plugin.Log.LogWarning($"[MV] {map.Key}: the assembly of plugin {map.PluginGuid} could not be resolved, so {target} was not called");
                    return false;
                }

                var type = assembly.GetType(target.TypeName, false);
                if (type == null)
                {
                    Plugin.Log.LogWarning($"[MV] {map.Key}: {assembly.GetName().Name} has no type '{target.TypeName}', so nothing was called");
                    return false;
                }

                var method = type.GetMethod(target.MethodName,
                                            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                                            null, Type.EmptyTypes, null);
                if (method == null || method.ContainsGenericParameters)
                {
                    Plugin.Log.LogWarning($"[MV] {map.Key}: '{target.TypeName}' has no static parameterless '{target.MethodName}', so nothing was called");
                    return false;
                }

                method.Invoke(null, null);
                Plugin.Log.LogInfo($"[MV] {map.Key}: asked {AssemblyName(map)} to stand down; called {target}()");
                return true;
            }
            catch (Exception e)
            {
                // Returned rather than thrown, because the caller turns a false into the refusal.
                Plugin.Log.LogError($"[MV] {map.Key}: calling '{spec}' to stand {AssemblyName(map)} down threw: {e}");
                return false;
            }
        }

        static readonly HashSet<string> UnmanagedReported = new HashSet<string>();

        static void AskIfUnanswered(string locationId)
        {
            if (_asked || SessionChoice != null) return;
            var answer = VariantSync.Ask(locationId, out var unmanaged);
            if (answer == null)
            {
                // A definitive "unmanaged" is latched; a failed call is not, so a network blip cannot
                // make this client answer for every later raid in the process.
                if (unmanaged)
                {
                    if (UnmanagedReported.Add(locationId))
                        Plugin.Log.LogError($"[MV] the server does not manage '{locationId}': it found no backport there, or discovery failed. "
                                            + "Nothing is suspended and the raid runs the BACKPORT, untouched.");
                    return;
                }
                Plugin.Log.LogError($"[MV] the server did not answer for '{locationId}'; falling back to the BACKPORT, "
                                    + "which is the resting state of the server's table");
                return;
            }
            _asked = true;
            SetChoice(locationId, answer);
            Plugin.Log.LogInfo($"[MV] nobody answered a prompt this raid; the server says '{answer}' for '{locationId}'");
        }

        static void ForgetChoiceIfStale(string locationId)
        {
            // If a raid end was ever missed, a stale answer must not decide a different map's raid.
            if (SessionLocationId == null || SessionLocationId == locationId) return;
            Plugin.Log.LogInfo($"[MV] a choice for '{SessionLocationId}' is stale on '{locationId}'; forgetting it");
            ForgetChoice();
        }

        // Records only what came back, so the wire table stays the statement of record.
        static void PostConfiguredDefault(ManagedMap map, string locationId)
        {
            var configured = ClientMaps.DefaultFor(map);
            var recorded = VariantSync.Post(locationId, configured);
            if (recorded == null)
                Plugin.Log.LogError($"[MV] prompt is off and the server did not acknowledge this client's default "
                                    + $"'{configured}' for '{locationId}'; the raid runs the BACKPORT");
            else
            {
                SetChoice(locationId, recorded);
                Plugin.Log.LogInfo($"[MV] prompt is off; posted this client's default '{configured}' for '{locationId}'");
            }
        }

        // Once a preset is loading, making the server agree is the only way left to keep its data
        // and these scenes on the same map.
        static void AdoptPatchStateIfNeeded(ManagedMap map, string locationId, string willLoad, int live)
        {
            if (!SuspendPolicy.MustAdoptPatchState(SessionChoice, willLoad)) return;

            Plugin.Log.LogError($"[MV] {map.Key}: the patch state disagrees with this raid's choice - "
                                + $"'{SessionChoice ?? "no answer"}' was recorded but {live} of {AssemblyName(map)}'s patch(es) "
                                + $"are live, so {Name(willLoad)} is what loads. The patch state wins: it cannot be changed "
                                + "safely once a preset is loading, so the choice is set to match and the server is told, "
                                + "which keeps its data and these scenes on the same map.");
            SetChoice(locationId, willLoad);
            if (VariantSync.Post(locationId, willLoad) == null)
                Plugin.Log.LogError($"[MV] {map.Key}: the server did not acknowledge '{willLoad}'; it may be serving the other "
                                    + "variant's data for this raid");
        }

        // Observes the load path, it does not decide it: the patch set cannot be changed safely
        // from here, so the client adopts whatever will load. See docs/DESIGN-NOTES.md.
        static bool Before(ScenesPreset preset)
        {
            try
            {
                if (!preset) return true;
                if (Children.Remove(preset)) return true;
                foreach (var child in preset.ChildPresets ?? new ScenesPreset[0])
                    if (child) Children.Add(child);

                // A preset with no ServerName is a REGISTERED PROVIDER's own scenes, and this prefix
                // has nothing to say about them: the provider was given its answer at the decision
                // point. Do not "fix" this by resolving a location some other way.
                var locationId = ManagedMap.Normalise(preset.ServerName);
                if (locationId.Length == 0) return true;
                var map = ClientMaps.Set.For(locationId);
                if (map == null) return true;
                if (ClientMaps.ReportIfBackportMissing(map)) return true;

                ForgetChoiceIfStale(locationId);

                // Asked BEFORE the prompt-off fallback below. On the path where that fallback matters
                // the raid's location was already generated, so this POST lands too late to change it
                // and the server's own answer has to win.
                AskIfUnanswered(locationId);

                if (SessionChoice == null && !ClientMaps.Prompt) PostConfiguredDefault(map, locationId);

                // No answer means the backport, which is what the server's table rests on, never this
                // client's configured default.
                var variant = SessionChoice != null ? MapVariant.Normalise(SessionChoice) : MapVariant.Backport;
                var callback = ClientMaps.Callback(map);
                if (callback != null)
                {
                    // The only notice a session that passed no decision point ever gets. A provider
                    // handed the same answer twice is told nothing new.
                    NotifyProvider(map, callback, variant);
                    Plugin.Log.LogInfo($"[MV] raid on '{locationId}': {Name(variant)}; provider notified");
                    return true;
                }

                // The patch state is the fact; the recorded choice is only an intention.
                var live = Selected(map).Count;
                var willLoad = SuspendPolicy.WillLoad(live);

                // The last word on whether the backport actually ran, for a session that reached a
                // raid without passing a decision point.
                if (MapVariant.IsBackport(willLoad)) _backportLoadedThisSession.Add(map.Key);

                AdoptPatchStateIfNeeded(map, locationId, willLoad, live);

                Plugin.Log.LogInfo($"[MV] raid on '{locationId}': {Name(willLoad)}; {live} of {AssemblyName(map)}'s patch(es) live, "
                                   + $"suspended {SuspendedCount(map)}");
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[MV] prefix failed, the load continues as patched: {e}");
                return true;
            }
        }
    }
}
