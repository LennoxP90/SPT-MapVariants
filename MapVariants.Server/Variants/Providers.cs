using JetBrains.Annotations;
using MapVariants.Shared;
using SPTarkov.DI.Annotations;
using Location = SPTarkov.Server.Core.Models.Eft.Common.Location;

namespace MapVariants.Server.Variants;

/// <summary>
/// The registration surface for a first-party mod that owns its own variant.
/// Resolve it from the DI container by type name, so no compile-time reference
/// to this assembly is needed.
/// </summary>
[Injectable(InjectionType.Singleton), UsedImplicitly]
public class Providers(VariantRegistry registry)
{
    // Call at PostLoad with the two Location objects.
    public void Register(string locationId, Location original, Location backport)
        => registry.RegisterProvider(locationId, original, backport);

    // Call before the first raid, once per extra variant built on this backport. False for an empty
    // or reserved id. The variant installs the backport's data; the caller adds its own on top.
    public bool DeclareVariant(string variant) => MapVariant.Declare(variant);

    /// <summary>
    /// API 3.2: for a mod that owns its own variant of a location another mod may also offer one
    /// of. Call at PostLoad, before MapVariants' discovery, with the variant's own Location. The
    /// stock side is the shipped map MapVariants captured itself, so nothing else is passed.
    /// variantId is the wire word, lower-cased, and must not be original, backport or unmanaged;
    /// the client registers the same id through Maps.RegisterSource. False means refused, and the
    /// log says why.
    /// </summary>
    public bool RegisterSource(string locationId, string variantId, Location variant)
        => registry.RegisterSource(locationId, variantId, variant);

    /// <summary>
    /// API 3.2: the shipped location as captured before any mod's Preload, to build a variant from.
    /// At PostLoad the live location may already be another mod's backport. The objects inside are
    /// SPT's own, so copy anything before changing it. Null for a location not captured.
    /// </summary>
    public Location? Shipped(string locationId) => registry.Shipped(locationId);

    // Read by reference against the captured field sets. An unmanaged location also answers
    // "original", so a caller that must tell those apart asks /mapvariants/managed. A source's id
    // comes back as itself, such as "classic".
    public string InstalledVariant(string locationId) => registry.InstalledVariant(locationId);
}
