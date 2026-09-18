#nullable enable
using System;
using System.Collections.Generic;

namespace MapVariants.Shared
{
    /// <summary>
    /// The maps this side can switch, each covering one or more location ids. A
    /// location belongs to one map, so a second claim on it is refused.
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

        public ManagedMap? For(string? locationId)
            => _byId.TryGetValue(ManagedMap.Normalise(locationId), out var map) ? map : null;

        public bool IsManaged(string? locationId) => For(locationId) != null;

        // IsManaged named for its call site: a transit prompts only for the map it is going to.
        public bool IsManagedDestination(string? destination) => IsManaged(destination);
    }
}
