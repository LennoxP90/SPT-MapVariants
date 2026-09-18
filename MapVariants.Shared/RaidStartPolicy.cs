#nullable enable
using System;

namespace MapVariants.Shared
{
    /// <summary>
    /// Whether a raid about to load still needs the server asked what it is
    /// serving. A client that passed the map screen or a transit claim has
    /// already applied its answer and must never be overridden.
    /// </summary>
    public static class RaidStartPolicy
    {
        // choiceLocationId is what the recorded choice was made FOR, not what is loading: on a
        // transit the previous raid's choice is still recorded and says nothing about this one.
        public static bool NeedsServerAnswer(string? locationId, string? choiceLocationId, string? sessionChoice)
        {
            var id = ManagedMap.Normalise(locationId);
            if (id.Length == 0) return false;
            if (string.IsNullOrEmpty(sessionChoice)) return true;
            return !string.Equals(ManagedMap.Normalise(choiceLocationId), id, StringComparison.Ordinal);
        }
    }
}
