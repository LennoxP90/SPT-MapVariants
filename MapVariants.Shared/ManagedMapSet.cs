#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace MapVariants.Shared
{
    /// <summary>
    /// The maps this side can switch, each covering one or more location ids. A
    /// location belongs to one map, so a second claim on it is refused; a source
    /// joins the map already there through Replace instead.
    /// </summary>
    public sealed class ManagedMapSet
    {
        private readonly List<ManagedMap> _maps = new List<ManagedMap>();
        private readonly Dictionary<string, ManagedMap> _byId = new Dictionary<string, ManagedMap>(StringComparer.Ordinal);

        public IReadOnlyList<ManagedMap> All => _maps;

        public void Add(ManagedMap map)
        {
            foreach (var id in map.LocationIds)
                if (_byId.ContainsKey(id)) throw new ArgumentException($"location '{id}' is already managed by '{_byId[id].Key}'");
            foreach (var id in map.LocationIds) _byId[id] = map;
            _maps.Add(map);
        }

        /// <summary>
        /// Swaps a map for its merged form, in place, so the display order is kept. The merged map
        /// must cover exactly the same location ids: a merge never moves a location between maps.
        /// </summary>
        public void Replace(ManagedMap existing, ManagedMap merged)
        {
            var index = _maps.IndexOf(existing);
            if (index < 0) throw new ArgumentException($"'{existing.Key}' is not in this set", nameof(existing));
            if (!existing.LocationIds.OrderBy(x => x, StringComparer.Ordinal)
                    .SequenceEqual(merged.LocationIds.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal))
                throw new ArgumentException($"a merge of '{existing.Key}' must cover the same location ids", nameof(merged));
            _maps[index] = merged;
            foreach (var id in merged.LocationIds) _byId[id] = merged;
        }

        // The one map every id belongs to, or null when they are unmanaged, split across maps, or
        // cover only part of a map: a source joins a whole map or none.
        public ManagedMap? Exactly(IEnumerable<string?> locationIds)
        {
            var ids = locationIds.Select(ManagedMap.Normalise).Where(id => id.Length > 0).Distinct().ToArray();
            if (ids.Length == 0) return null;
            var map = For(ids[0]);
            if (map == null || ids.Any(id => !ReferenceEquals(For(id), map)) || ids.Length != map.LocationIds.Length) return null;
            return map;
        }

        // Whether any of these ids is managed at all.
        public bool AnyManaged(IEnumerable<string?> locationIds) => locationIds.Any(IsManaged);

        public ManagedMap? For(string? locationId)
            => _byId.TryGetValue(ManagedMap.Normalise(locationId), out var map) ? map : null;

        public bool IsManaged(string? locationId) => For(locationId) != null;

        // IsManaged named for its call site: a transit prompts only for the map it is going to.
        public bool IsManagedDestination(string? destination) => IsManaged(destination);
    }
}
