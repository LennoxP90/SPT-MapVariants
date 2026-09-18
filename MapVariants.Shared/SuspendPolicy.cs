#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace MapVariants.Shared
{
    /// <summary>
    /// One Harmony patch on the scene-load method, flattened to primitives so the
    /// decisions below can be tested with no game and no Harmony.
    /// </summary>
    public sealed class RecordedPatch
    {
        public string Kind { get; }
        public string Owner { get; }
        public string AssemblyName { get; }
        public string MethodName { get; }
        public int Priority { get; }

        public RecordedPatch(string kind, string owner, string assemblyName, string methodName, int priority)
        {
            Kind = kind; Owner = owner; AssemblyName = assemblyName; MethodName = methodName; Priority = priority;
        }

        public override string ToString() => $"{Kind} {Owner}:{MethodName} p{Priority} [{AssemblyName}]";
    }

    /// <summary>
    /// Whose patches to remove for a shipped-map raid, and what the client will
    /// actually load once that is done.
    /// </summary>
    public static class SuspendPolicy
    {
        // Identity is the declaring ASSEMBLY: SPT's ModulePatch registers under the patch class name,
        // which two plugins can share. ownAssemblyName is excluded unconditionally, so a maps.json
        // entry naming this plugin cannot make the caller unpatch itself from inside its own prefix.
        public static RecordedPatch[] Select(IEnumerable<RecordedPatch> all, string pluginAssemblyName, string ownAssemblyName)
            => all.Where(p => string.Equals(p.AssemblyName, pluginAssemblyName, StringComparison.Ordinal)
                           && !string.Equals(p.AssemblyName, ownAssemblyName, StringComparison.Ordinal))
                  .ToArray();

        // stillLive is measured AFTER the attempt. Counting the selection instead broke two
        // consecutive shipped raids; see docs/DESIGN-NOTES.md.
        public static bool MustWithdrawVeto(bool wantStock, int stillLive) => wantStock && stillLive > 0;

        public static bool PatchSetChanged(int recordedBefore, int recordedAfter) => recordedBefore != recordedAfter;

        // Read off the live patch state, not off anyone's intention: one patch still live and the
        // backport takes the whole load over.
        public static string WillLoad(int backportPatchesStillLive)
            => backportPatchesStillLive > 0 ? MapVariant.Backport : MapVariant.Original;

        // Asked only from the scene-load path, where the patch set can no longer be changed safely.
        public static bool MustAdoptPatchState(string? sessionChoice, string willLoad)
            => VariantResolution.Resolve(sessionChoice, null) != MapVariant.Normalise(willLoad);
    }
}
