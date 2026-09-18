using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Utils.Json;

namespace MapVariants.Server.Variants;

/// <summary>
/// The seven fields a backport replaces on a Location, held by reference. Capturing
/// and assigning these rather than swapping the Location object keeps the dictionary
/// entry and every holder of the object valid, and is what the backports do too.
/// </summary>
public sealed class LocationFieldSet
{
    public LocationBase Base { get; private set; } = null!;
    public LazyLoad<LooseLoot>? LooseLoot { get; private set; }
    public LazyLoad<Dictionary<MongoId, StaticLootDetails>>? StaticLoot { get; private set; }
    public LazyLoad<StaticContainerDetails>? StaticContainers { get; private set; }
    public Dictionary<string, IEnumerable<StaticAmmoDetails>> StaticAmmo { get; private set; } = null!;
    public StaticContainer? Statics { get; private set; }
    public IEnumerable<AllExtractsExit> AllExtracts { get; private set; } = null!;

    public static LocationFieldSet Capture(Location location) => new()
    {
        Base = location.Base,
        LooseLoot = location.LooseLoot,
        StaticLoot = location.StaticLoot,
        StaticContainers = location.StaticContainers,
        StaticAmmo = location.StaticAmmo,
        Statics = location.Statics,
        AllExtracts = location.AllExtracts,
    };

    public void AssignTo(Location location)
    {
        location.Base = Base;
        location.LooseLoot = LooseLoot;
        location.StaticLoot = StaticLoot;
        location.StaticContainers = StaticContainers;
        location.StaticAmmo = StaticAmmo;
        location.Statics = Statics;
        location.AllExtracts = AllExtracts;
    }

    // In the same order as the fields above; DiscoveryPolicy compares these.
    public object?[] References => [Base, LooseLoot, StaticLoot, StaticContainers, StaticAmmo, Statics, AllExtracts];

    public bool IsLive(Location location) => ReferenceEquals(location.Base, Base);
}
