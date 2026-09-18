using System.Collections.Concurrent;
using JetBrains.Annotations;
using MapVariants.Shared;
using SPTarkov.DI.Annotations;

namespace MapVariants.Server.Variants;

/// <summary>
/// The variant the first player into a transit zone claimed, held until a raid there
/// starts or ends. Keyed by location, not by session, so everyone who joins gets the
/// same answer; the owner is kept so only they can change their mind.
/// </summary>
[Injectable(InjectionType.Singleton), UsedImplicitly]
public class TransitClaimStore
{
    private readonly ConcurrentDictionary<string, (string Variant, string Owner)> _claims = new(StringComparer.Ordinal);

    public enum Outcome
    {
        Claimed,
        Changed,
        Joined,
    }

    // TryAdd is what makes the race safe: it returns true only for the caller that inserted, so two
    // players entering the zone in the same frame cannot both be told they were first.
    public string Claim(string? locationId, string? variant, string owner, out Outcome outcome)
    {
        var offered = MapVariant.Normalise(variant);
        var key = LocationId.Normalise(locationId);

        if (_claims.TryAdd(key, (offered, owner)))
        {
            outcome = Outcome.Claimed;
            return offered;
        }

        // Released between the TryAdd and here, so nobody holds it: take it.
        if (!_claims.TryGetValue(key, out var held))
        {
            _claims[key] = (offered, owner);
            outcome = Outcome.Claimed;
            return offered;
        }

        if (held.Owner != owner)
        {
            outcome = Outcome.Joined;
            return held.Variant;
        }

        _claims[key] = (offered, owner);
        outcome = held.Variant == offered ? Outcome.Claimed : Outcome.Changed;
        return offered;
    }

    public string Held(string? locationId)
        => _claims.TryGetValue(LocationId.Normalise(locationId), out var claim) ? claim.Variant : "";

    public void Release(string? locationId) => _claims.TryRemove(LocationId.Normalise(locationId), out _);
}
