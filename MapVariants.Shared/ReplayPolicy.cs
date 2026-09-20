#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace MapVariants.Shared
{
    /// <summary>
    /// The parts of one location a load-time mod is expected to change: the scalar
    /// properties of the base, the scalar properties of each exit by name, and whether
    /// each collection was empty.
    /// </summary>
    public sealed class LocationValues
    {
        public IReadOnlyDictionary<string, object?> Scalars { get; }
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> Exits { get; }
        public IReadOnlyDictionary<string, bool> CollectionsEmpty { get; }

        public LocationValues(IReadOnlyDictionary<string, object?>? scalars = null,
                              IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>? exits = null,
                              IReadOnlyDictionary<string, bool>? collectionsEmpty = null)
        {
            Scalars = scalars ?? new Dictionary<string, object?>();
            Exits = exits ?? new Dictionary<string, IReadOnlyDictionary<string, object?>>();
            CollectionsEmpty = collectionsEmpty ?? new Dictionary<string, bool>();
        }
    }

    /// <summary>What to write onto the shipped map. Anything absent is left as it shipped.</summary>
    public sealed class ReplayPlan
    {
        public IReadOnlyDictionary<string, object?> Scalars { get; }
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> Exits { get; }
        public IReadOnlyList<string> Cleared { get; }

        public ReplayPlan(IReadOnlyDictionary<string, object?> scalars,
                          IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> exits,
                          IReadOnlyList<string> cleared)
        {
            Scalars = scalars;
            Exits = exits;
            Cleared = cleared;
        }

        public static ReplayPlan Empty { get; } = new ReplayPlan(
            new Dictionary<string, object?>(),
            new Dictionary<string, IReadOnlyDictionary<string, object?>>(),
            new string[0]);

        public bool IsEmpty => Count == 0;

        public int Count => Scalars.Count + Exits.Sum(e => e.Value.Count) + Cleared.Count;

        /// <summary>One line naming what the plan carries, for the log.</summary>
        public string Describe()
        {
            var parts = new List<string>();
            if (Scalars.Count > 0) parts.Add("base[" + string.Join(",", Scalars.Keys) + "]");
            foreach (var exit in Exits) parts.Add($"'{exit.Key}'[" + string.Join(",", exit.Value.Keys) + "]");
            if (Cleared.Count > 0) parts.Add("cleared[" + string.Join(",", Cleared) + "]");
            return parts.Count == 0 ? "nothing" : string.Join(" ", parts);
        }
    }

    /// <summary>
    /// A backport is installed during Preload, so a mod that configures locations at load
    /// configures the backport's data and never the shipped data. Diffing the backport as it
    /// is now against a reading taken before those mods ran gives what they changed, which is
    /// the only part safe to copy onto the shipped map.
    ///
    /// Three rules:
    ///   - a scalar both readings know, whose value differs, is replayed;
    ///   - an exit both maps have by name has its changed scalars replayed;
    ///   - a collection that went from non-empty to empty is cleared.
    /// Filling a collection is never replayed, because a list a mod filled can name zones only
    /// the backport has.
    /// </summary>
    public static class ReplayPolicy
    {
        public static ReplayPlan Compute(LocationValues pristine, LocationValues current)
        {
            var scalars = ChangedValues(pristine.Scalars, current.Scalars);

            var exits = new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.Ordinal);
            foreach (var pair in current.Exits)
            {
                if (!pristine.Exits.TryGetValue(pair.Key, out var before)) continue;
                var changed = ChangedValues(before, pair.Value);
                if (changed.Count > 0) exits[pair.Key] = changed;
            }

            var cleared = current.CollectionsEmpty
                .Where(pair => pair.Value
                               && pristine.CollectionsEmpty.TryGetValue(pair.Key, out var wasEmpty)
                               && !wasEmpty)
                .Select(pair => pair.Key)
                .ToArray();

            return new ReplayPlan(scalars, exits, cleared);
        }

        // A key only one side knows is skipped rather than guessed at.
        private static Dictionary<string, object?> ChangedValues(
            IReadOnlyDictionary<string, object?> before, IReadOnlyDictionary<string, object?> after)
        {
            var changed = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var pair in after)
            {
                if (!before.TryGetValue(pair.Key, out var was)) continue;
                if (!Equals(was, pair.Value)) changed[pair.Key] = pair.Value;
            }
            return changed;
        }
    }
}
