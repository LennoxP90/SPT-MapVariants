using System.Linq;
using System.Text.Json.Serialization;
using JetBrains.Annotations;
using MapVariants.Shared;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;

namespace MapVariants.Server.Variants;

// The body of a raid start or end. Only the location is read.
public sealed class RaidBoundaryRequest : IRequestData
{
    [JsonPropertyName("location")] public string Location { get; set; } = "";
    [JsonPropertyName("serverId")] public string ServerId { get; set; } = "";
}

/// <summary>
/// A backstop for a raid that reached the start route with no choice posted, and
/// the return to the backport afterwards. The table is kept correct at all times
/// rather than swapped here; the timing is in docs/DESIGN-NOTES.md.
/// </summary>
// The priority puts this router BEFORE SPT's own, whose match router generates the location.
[Injectable(TypePriority = OnLoadOrder.Routers - 1), UsedImplicitly]
public class VariantRaidHooks(
    JsonUtil jsonUtil,
    VariantRegistry registry,
    VariantChoiceStore choiceStore,
    TransitClaimStore transitClaims,
    ISptLogger<VariantRaidHooks> logger)
    : StaticRouter(jsonUtil,
    [
        // The earliest route that names the location. It lands before Fika creates the raid, so it
        // is the one that matters on a Fika host; a headless posts it too.
        new RouteAction<RaidBoundaryRequest>("/client/raid/configuration",
            (url, info, sessionId, output, cancellationToken) =>
                OnRaidStart(info, sessionId, output ?? "", registry, choiceStore, transitClaims, logger)),

        new RouteAction<RaidBoundaryRequest>("/client/match/local/start",
            (url, info, sessionId, output, cancellationToken) =>
                OnRaidStart(info, sessionId, output ?? "", registry, choiceStore, transitClaims, logger)),

        new RouteAction<RaidBoundaryRequest>("/client/match/local/end",
            (url, info, sessionId, output, cancellationToken) =>
                OnRaidEnd(info, output ?? "", registry, transitClaims, logger)),
    ])
{
    private static ValueTask<string> OnRaidStart(
        RaidBoundaryRequest? info, MongoId sessionId, string output,
        VariantRegistry registry, VariantChoiceStore choiceStore, TransitClaimStore transitClaims,
        ISptLogger<VariantRaidHooks> logger)
    {
        var locationId = LocationId.Normalise(info?.Location);
        if (locationId.Length > 0 && registry.IsManaged(locationId))
        {
            try
            {
                // A live claim outranks everything on EVERY call: both raid-start routes run, so a
                // second one that re-resolved would overwrite the first.
                var claimed = transitClaims.Held(locationId);
                var ownChoice = choiceStore.ChoiceFor(sessionId, locationId);
                var latest = choiceStore.LatestFor(locationId);

                if (!VariantResolution.HasAnswer(claimed, ownChoice, latest))
                {
                    // The table already rests on the backport. Staying out matters because discovery
                    // also finds locations another mod manages.
                    logger.Debug($"[MV] raid on '{locationId}': no choice or claim recorded; leaving the table alone");
                    return ValueTask.FromResult(output);
                }

                // A Fika headless posts under its OWN session, so it inherits the location's last choice.
                var variant = claimed.Length > 0 ? claimed : VariantResolution.Resolve(ownChoice, latest);

                registry.Install(locationId, variant);
                logger.Info($"[MV] raid on '{locationId}': {(MapVariant.IsBackport(variant) ? "BACKPORT" : "ORIGINAL")}; the table serves {registry.Installed(locationId)}");
            }
            catch (Exception e)
            {
                logger.Error($"[MV] could not install a variant for '{locationId}'; the currently installed map is served: {e}");
            }
        }

        return ValueTask.FromResult(output);
    }

    private static ValueTask<string> OnRaidEnd(
        RaidBoundaryRequest? info, string output, VariantRegistry registry,
        TransitClaimStore transitClaims, ISptLogger<VariantRaidHooks> logger)
    {
        // EndLocalRaidRequestData has only serverId, and SPT derives the map from it. Reading
        // Location alone made this whole method dead code.
        var locationId = LocationId.Normalise(info?.Location);
        if (locationId.Length == 0) locationId = LocationId.Normalise(info?.ServerId?.Split('.').FirstOrDefault());

        if (locationId.Length > 0 && registry.IsManaged(locationId))
        {
            // Released here and not at raid start, because both raid-start routes run and the second
            // would find nothing left to prefer.
            transitClaims.Release(locationId);

            // Only this location, and only if we installed here: resetting every managed location
            // would wipe a claim another map just took on a transit.
            if (registry.HasInstalled(locationId))
            {
                registry.Install(locationId, MapVariant.Backport);
                logger.Debug($"[MV] raid over on '{locationId}'; that location's table is back to the backport");
            }
        }

        return ValueTask.FromResult(output);
    }
}
