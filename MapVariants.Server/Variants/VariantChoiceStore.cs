using System.Collections.Concurrent;
using JetBrains.Annotations;
using MapVariants.Shared;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;

namespace MapVariants.Server.Variants;

/// <summary>
/// The starting player's choice for a raid, keyed by session and location, plus the
/// latest choice for each location. In memory only.
/// </summary>
[Injectable(InjectionType.Singleton), UsedImplicitly]
public class VariantChoiceStore
{
    private readonly ConcurrentDictionary<string, string> _choices = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _latest = new(StringComparer.Ordinal);

    private static string Key(MongoId sessionId, string? locationId) => sessionId + "|" + LocationId.Normalise(locationId);

    public void Record(MongoId sessionId, string locationId, string variant)
    {
        var normalised = MapVariant.Normalise(variant);
        _choices[Key(sessionId, locationId)] = normalised;
        _latest[LocationId.Normalise(locationId)] = normalised;
    }

    // Empty, never Original, when nothing is recorded.
    public string LatestFor(string locationId)
        => _latest.TryGetValue(LocationId.Normalise(locationId), out var v) ? v : VariantResolution.NoAnswer;

    public string ChoiceFor(MongoId sessionId, string locationId)
        => _choices.TryGetValue(Key(sessionId, locationId), out var v) ? v : VariantResolution.NoAnswer;

    public void Forget(MongoId sessionId, string locationId) => _choices.TryRemove(Key(sessionId, locationId), out _);
}
