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

`MapVariants.Client.Api.Maps` exposes `CurrentVariant` (`original`, `backport`, or null for "not yet"),
`CurrentLocationId`, `IsBackport`, `IsManaged(locationId)`, `DisplayName(locationId, variant)` and the
`VariantResolved` event, all reflection-friendly. A mod that owns its own variant calls `Register(...)`.
