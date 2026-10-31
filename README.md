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
currently `3.1`: minor for an addition, major for anything a consumer could break on.

**Client, `MapVariants.Client.Api.Maps`:**

| member | meaning |
|---|---|
| `CurrentVariant` | The variant for this raid: `original`, `backport`, or a declared id such as `halloween2026`. **Null means "not decided yet"**, never `original`. |
| `CurrentLocationId` | The location that answer is for. |
| `Decided` | Whether `CurrentVariant` is known yet. |
| `IsBackport` | True for every variant except `original`: the backport and anything declared on top of it. |
| `IsVariant(id)` | Whether this raid is that exact variant (3.1). |
| `Variants(locationId)` | Every variant the map offers, `original` and `backport` first (3.1). |
| `IsManaged(locationId)` | Whether MapVariants manages that location. |
| `DisplayName(locationId, variant)` | The label a player sees, such as `Factory - Classic`. |
| `VariantResolved` | `Action<string>` event, raised with the variant once it is known, and again if the player changes their mind. |
| `Register(...)` | For a mod that owns its own backport: registers the two variants. |
| `RegisterVariants(...)` | As `Register`, plus further variants each built on the backport (3.1). |

**Server, `MapVariants.Server.Variants.Providers`** (resolve it from the DI container by type name):

| member | meaning |
|---|---|
| `InstalledVariant(locationId)` | The variant the location table is serving right now, declared ids included. Read it when needed; it changes with each player's pick. |
| `Register(locationId, original, backport)` | Hands over both `Location` objects, at `PostLoad`. |
| `DeclareVariant(id)` | Declares a further variant before the first raid (3.1). |

A declared variant serves the backport's server data and keeps the backport's client patches; the mod
that declared it adds its own content on top. FactoryClassic's `halloween2026` is the working example.
