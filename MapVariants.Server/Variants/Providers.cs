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

    // Read by reference against the two captured field sets. An unmanaged location also answers
    // "original", so a caller that must tell those apart asks /mapvariants/managed.
    public string InstalledVariant(string locationId) => registry.InstalledVariant(locationId);
}
