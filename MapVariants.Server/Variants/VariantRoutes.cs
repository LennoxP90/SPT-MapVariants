using System.Text.Json.Serialization;
using JetBrains.Annotations;
using MapVariants.Shared;
using Microsoft.AspNetCore.Http;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;

namespace MapVariants.Server.Variants;

// Must implement IRequestData. Without it this compiles and then the dispatcher throws on the first
// request.
public sealed class VariantChoiceRequest : IRequestData
{
    [JsonPropertyName("locationId")] public string LocationId { get; set; } = "";
    [JsonPropertyName("variant")] public string Variant { get; set; } = "";
}

// Our own type, not SPT's err/errMsg/data envelope, so the client deserialises it directly.
public sealed class VariantChoiceResponse
{
    [JsonPropertyName("variant")] public string Variant { get; set; } = MapVariant.Original;

    // Claimed, Changed or Joined, so the client can say whether the player decided or inherited.
    [JsonPropertyName("outcome")] public string Outcome { get; set; } = "";
}

public sealed class ManagedResponse
{
    [JsonPropertyName("locationIds")] public List<string> LocationIds { get; set; } = [];
}

/// <summary>
/// POST records the choice a player made on the map screen; GET reads one back. A
/// Fika headless uses the GET to find out which variant the starting player
/// picked, since it hosts under its own session.
/// </summary>
// The GET routes take EmptyRequestData because a RouteAction that declares a body type fails on the
// null body SPT hands every GET.
[Injectable, UsedImplicitly]
public class VariantRoutes(
    JsonUtil jsonUtil,
    HttpResponseUtil httpResponseUtil,
    VariantChoiceStore choiceStore,
    TransitClaimStore transitClaims,
    VariantRegistry registry,
    IHttpContextAccessor httpContextAccessor,
    ISptLogger<VariantRoutes> logger
) : StaticRouter(
    jsonUtil,
    [
        new RouteAction<VariantChoiceRequest>("/mapvariants/variant",
            (url, info, sessionId, output, ct) => ValueTask.FromResult(Record(info, sessionId, choiceStore, transitClaims, registry, httpResponseUtil, logger))),
        new RouteAction<VariantChoiceRequest>("/mapvariants/transitclaim",
            (url, info, sessionId, output, ct) => ValueTask.FromResult(ClaimTransit(info, sessionId, transitClaims, registry, httpResponseUtil, logger))),
        new RouteAction<EmptyRequestData>("/mapvariants/hostvariant",
            (url, info, sessionId, output, ct) => ValueTask.FromResult(Read(httpContextAccessor.HttpContext?.Request, registry, httpResponseUtil))),
        new RouteAction<EmptyRequestData>("/mapvariants/managed",
            (url, info, sessionId, output, ct) => ValueTask.FromResult(httpResponseUtil.NoBody(new ManagedResponse { LocationIds = registry.ManagedIds.ToList() }))),
    ])
{
    private static string Record(VariantChoiceRequest? info, MongoId sessionId, VariantChoiceStore choiceStore,
        TransitClaimStore transitClaims, VariantRegistry registry, HttpResponseUtil http, ISptLogger<VariantRoutes> logger)
    {
        var locationId = LocationId.Normalise(info?.LocationId);
        var variant = MapVariant.Normalise(info?.Variant);

        // Unmanaged, never original: "not managed" can also mean discovery failed on a map that has
        // a backport, and original there loads the shipped scenes over its data.
        if (locationId.Length == 0 || !registry.IsManaged(locationId))
            return http.NoBody(new VariantChoiceResponse { Variant = MapVariant.Unmanaged });

        // An explicit pick outranks a claim inherited from a transit, so the claim goes.
        transitClaims.Release(locationId);
        choiceStore.Record(sessionId, locationId, variant);

        // This POST lands while the player is still on the map screen, before the raid is built.
        registry.Install(locationId, variant);
        logger.Info($"[MV] {sessionId} chose '{variant}' for '{locationId}'; the table now serves {registry.Installed(locationId)}");
        return http.NoBody(new VariantChoiceResponse { Variant = variant });
    }

    // Without this each client falls back on its own preference, which on Fika means one player
    // loading scenes nobody is being served.
    private static string ClaimTransit(VariantChoiceRequest? info, MongoId sessionId, TransitClaimStore transitClaims,
        VariantRegistry registry, HttpResponseUtil http, ISptLogger<VariantRoutes> logger)
    {
        var locationId = LocationId.Normalise(info?.LocationId);
        if (locationId.Length == 0 || !registry.IsManaged(locationId))
            return http.NoBody(new VariantChoiceResponse { Variant = MapVariant.Unmanaged });

        var winner = transitClaims.Claim(locationId, info?.Variant, sessionId.ToString(), out var outcome);

        // Re-asserted on every outcome, a join included: once a raid end can run, a later joiner must
        // not be handed a variant the table has since stopped serving.
        registry.Install(locationId, winner);
        if (outcome != TransitClaimStore.Outcome.Joined)
        {
            logger.Info($"[MV] {sessionId} {(outcome == TransitClaimStore.Outcome.Changed ? "changed the transit to" : "claimed")} '{winner}' for '{locationId}'");
        }
        else
        {
            logger.Debug($"[MV] {sessionId} joined a transit to '{locationId}' already claimed as '{winner}'");
        }
        return http.NoBody(new VariantChoiceResponse { Variant = winner, Outcome = outcome.ToString() });
    }

    // What the table is actually serving, so a Fika joiner and a headless load the scenes that
    // match the data they are about to be handed.
    private static string Read(HttpRequest? request, VariantRegistry registry, HttpResponseUtil http)
    {
        var locationId = LocationId.Normalise(request?.Query["locationId"].ToString());
        var variant = registry.IsManaged(locationId) ? registry.InstalledVariant(locationId) : MapVariant.Unmanaged;
        return http.NoBody(new VariantChoiceResponse { Variant = variant });
    }
}
