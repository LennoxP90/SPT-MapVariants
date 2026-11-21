# MapVariants

Lets you choose, per raid, between the map SPT ships and a backport of it. Ships configured for
ManimalLighthouse and ManimalInterchange. The backport is not modified in any way; on a raid where you
pick it, it runs exactly as it does without this mod.

## Install

Extract over your SPT folder. Both halves are needed: `BepInEx\plugins\LennoxP90-MapVariants` on every
client and the headless, `SPT_Runtime\user\mods\LennoxP90-MapVariants` on the server.

## Use

Pick a map with a backport on the map screen and press Next: a prompt asks which version to load. In
F12, `PromptSelection` turns the prompt off and each map's `DefaultSelection` then decides. Entering a
transit zone that leads to one of these maps claims your default for everyone who joins; you can
change it on the transfer screen. When nobody chooses, the backport loads.

## Adding a map

Add an entry to `maps.json` beside the client DLL:

    { "locationIds": ["lighthouse"], "pluginGuid": "com.manimal.lighthouse",
      "displayName": "Lighthouse", "backportName": "Manimal", "originalName": "Vanilla" }

The map name is declared once, in `displayName`, and each variant is named on its own. F12 offers the
variant names alone under a section already headed with the map name; the prompt tiles, the loading
screen and the transit message compose `Lighthouse - Manimal` from the two.

The server needs nothing: it notices any location whose data a mod replaced at load. Optional tiles go
in `tiles/<locationId>-original.png` and `tiles/<locationId>-backport.png`.

## For mod authors

Everything below is reflection-friendly: consume it after a soft `BepInDependency` on
`com.lennoxp90.mapvariants`, with no compile-time reference. `ApiVersion` is the contract version,
currently `3.2`: minor for an addition, major for anything a consumer could break on.

**Client, `MapVariants.Client.Api.Maps`:**

| member | meaning |
|---|---|
| `CurrentVariant` | The variant for this raid: `original`, `backport`, or a declared id such as `halloween2026`. **Null means "not decided yet"**, never `original`. |
| `CurrentLocationId` | The location that answer is for. |
| `Decided` | Whether `CurrentVariant` is known yet. |
| `IsBackport` | True for every variant except `original`: the backport, anything declared on top of it, and every source's variant. |
| `IsVariant(id)` | Whether this raid is that exact variant (3.1). |
| `Variants(locationId)` | Every variant the map offers, `original` and `backport` first, sources last (3.1). |
| `IsManaged(locationId)` | Whether MapVariants manages that location. |
| `DisplayName(locationId, variant)` | The label a player sees, such as `Factory - Classic`. |
| `VariantResolved` | `Action<string>` event, raised with the variant once it is known, and again if the player changes their mind. |
| `Register(...)` | For a mod that owns its own backport: registers the two variants. |
| `RegisterVariants(...)` | As `Register`, plus further variants each built on the backport (3.1). |
| `RegisterSource(...)` | For a mod that owns its own variant of a map another mod may also offer one of: adds one variant under its own id (3.2). See below. |

**Server, `MapVariants.Server.Variants.Providers`** (resolve it from the DI container by type name):

| member | meaning |
|---|---|
| `InstalledVariant(locationId)` | The variant the location table is serving right now, declared ids included. Read it when needed; it changes with each player's pick. |
| `Register(locationId, original, backport)` | Hands over both `Location` objects, at `PostLoad`. |
| `DeclareVariant(id)` | Declares a further variant before the first raid (3.1). |
| `RegisterSource(locationId, variantId, variant)` | Hands over a source's own `Location`, at `PostLoad` (3.2). |
| `Shipped(locationId)` | The shipped `Location`, as captured before any mod's `Preload`, to build a source from (3.2). |

A declared variant serves the backport's server data and keeps the backport's client patches; the mod
that declared it adds its own content on top. FactoryClassic's `halloween2026` is the working example.

### Several mods on one map (3.2)

A map can be offered by its backport (a `maps.json` entry or `Register`) and by any number of
**sources** at once, each owning one variant under its own id. Customs with FumsCustoms (the backport,
from `maps.json`) and CustomsClassic (a source, id `classic`) gives three tiles: Vanilla, Fums and
Classic. A source can also stand alone, with no backport on that map.

- **Ids.** A source's id is its wire word, such as `classic`. It must not be `original`, `backport` or
  `unmanaged`, and only one mod may use it on a map. Register the same id on both sides.
- **Server.** At `PostLoad`, build the variant from `Shipped(locationId)` rather than from the live
  location, which may already hold another mod's backport. Copy anything before changing it: the
  objects are SPT's own. Then call `RegisterSource`. A source is installed at MapVariants' discovery,
  not at registration, so a backport that builds on the live location later in `PostLoad` still builds
  on the shipped map.
- **Client.** Call `Maps.RegisterSource(locationIds, displayName, variantId, variantName, originalName,
  variantTile, originalTile, onVariant, warnings)` from `Awake`, after a hard `BepInDependency`. The
  callback hears two words whatever else the map offers: `backport` when your variant is chosen and
  `original` for anything else, so a source stands down when another mod's variant is picked. Warnings
  show on your tile only. `displayName` and `originalName` are used only when your call creates the map.
- **Resting variant.** Between raids and when nobody chose, the table rests on the backport, or on a
  map with no backport, on the source whose id sorts first. Both sides apply the same rule.
- **The backport.** When a source's variant is chosen, the backport's patches on the scene load are
  suspended and its stand-down runs, exactly as for a Vanilla raid. A backport whose plugin is not
  loaded drops out and the sources stay on offer.
- **Every client and the headless** need the same sources installed: a variant id a client has never
  heard of reads as `original` there.

Load-time configuration replay (an SVM-style mod's values copied onto the shipped map) still runs from
the backport only; a source serves the values it was built with. Likewise, a mod that edits the live
location after a source registered (a spawn mod later in `PostLoad`) edits the shipped map or the
backport, not the source: a source that wants those edits registers late in `PostLoad`, before
MapVariants' discovery at `PostLoad + 500000`.
