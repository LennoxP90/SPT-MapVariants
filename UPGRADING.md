# Upgrading to MapVariants 1.2.0 (extension API 3.3)

Nothing breaks. A mod written against API 3.0, 3.1 or 3.2 keeps working on 1.2.0 without a change: every
existing member, every callback word and the `maps.json` format are as they were. This guide is for a mod
that wants what 1.2.0 adds, or that needs to know what its players will now see. [API.md](API.md) is the
full reference.

## What changed

**For players**

- **Several mods can offer a version of the same map.** Before 1.2.0 a map belonged to one mod; a second
  mod registering the same map was refused. Now one backport and any number of sources share it, one tile
  each.
- **Vanilla is the F12 default.** A new `DefaultSelection` entry starts as the map SPT ships, not as your
  version, and a value MapVariants does not recognise (a version from a mod since removed) reads as Vanilla
  too. Stored picks are kept. A player opts into your version on the map screen or in F12.
- **Pages past four versions.** The choice window shows four tiles at a time, turned with chevrons, Q/E or
  Page Up/Down.
- **No description line under a tile.** The tile shows its name only, so a version's name should say what
  it is.

**In the API**

| Member | Side | Since | Purpose |
|---|---|---|---|
| `Maps.RegisterSource(...)` | client | 3.2 | Offer your own version of a map beside other mods' versions |
| `Providers.RegisterSource(locationId, variantId, location)` | server | 3.2 | The server half of the same |
| `Providers.Shipped(locationId)` | server | 3.2 | The shipped location, to build your version from |
| `Providers.DeclarePlugin(locationId, clientPluginGuid)` | server | 3.3 | Opt-in: lets clients check the server serves your backport, not another mod's |
| `Maps.RegisterBackport(...)` | client | 3.3 | A `maps.json` entry made at runtime, no file edit or restart |
| `Maps.SelectedId` | client | 3.3 | Whose version loaded, as `owner:variant` (`spt:shipped` for the map SPT ships) |
| `Maps.SelectedOwner` | client | 3.3 | The plugin GUID of the mod that registered it |
| `Maps.SelectedName` | client | 3.3 | The label the player saw on the tile |
| `Maps.IdFor(locationId, variant)` | client | 3.3 | The same id for any variant of any map |

`Maps.ApiVersion` reports `3.3`.

## If your mod registers a version of a map

### You need do nothing when

- your map is a `maps.json` entry, or you call `Register` / `RegisterVariants`, **and**
- no other mod offers a version of the same map to your players.

Your callback hears the same words as before, your tile and name are unchanged, and `SelectedId` names your
version with no change on your side: its owner is read off the callback you already register (or, for a
`maps.json` entry, its `pluginGuid`).

### Move to `RegisterSource` when another mod may offer the same map

A map still takes **one** backport. A second `Register` or `maps.json` entry on a map that already has one is
refused, exactly as in 1.1.0. Sources do not compete: any number of them share a map with each other and
with its backport. If your players may install another mod for the same map, register as a source.

1. **Detect 1.2.0** and keep the old path for older installs. A source needs `RegisterSource` on both sides:

   ```csharp
   // client
   var maps = AppDomain.CurrentDomain.GetAssemblies()
       .Select(a => a.GetType("MapVariants.Client.Api.Maps", false)).FirstOrDefault(t => t != null);
   var registerSource = maps?.GetMethod("RegisterSource");      // null before 1.2.0: use Register as before

   // server, on the Providers type resolved from the container
   var serverSource = providersType.GetMethod("RegisterSource");
   var shipped = providersType.GetMethod("Shipped");
   ```

2. **Build your server data from `Shipped(locationId)`**, at `PostLoad`, never from the live location: by
   then it may already hold another mod's backport, and your version would inherit its spawns, exits and
   loot. The objects inside are SPT's own; copy anything before you change it. Hand it over with
   `RegisterSource(locationId, yourId, location)` before MapVariants' discovery at `PostLoad + 500000`.

3. **Register the client half** from `Awake`, after a hard `BepInDependency`, with the **same id**:

   ```csharp
   Maps.RegisterSource(locationIds, displayName, "myversion", "My Version", "Vanilla",
                       variantTile, originalTile, onVariant, warnings);
   ```

   The id is lower case and must not be `original`, `backport` or `unmanaged`. `displayName` and
   `originalName` are used only if your call is the first to offer that map.

4. **Your callback does not change.** It still hears `backport` when your version is chosen and `original`
   for anything else, including another mod's version, so a callback that already switches on those two
   words is correct as it stands.

5. **Translate the new word where you read state back.** For a source, `Maps.CurrentVariant` and
   `Providers.InstalledVariant` return **your id** (`myversion`), not `backport`. Code that compares either
   of them to `backport` must accept your id as well.

6. **Drop any workaround that made your mod stand down** beside another mod for the same map, but only when
   both of you are sources (MapVariants 1.2.0 or later, and the other mod's version that registers as a
   source). With an older MapVariants or an older copy of the other mod, the one-backport rule still applies
   and standing down is still right.

### Do not reach into MapVariants' internals

Only `MapVariants.Client.Api.Maps` and `MapVariants.Server.Variants.Providers` are the API. Internal types
(the client's map set, its manifest parser, its registration helpers) can change in any release without
notice. A mod that adds its own `maps.json` entry at runtime should call `Maps.RegisterBackport` (3.3) instead:
the same entry, through the same parser, with no file to write and no restart. Keep the old route only for an
older MapVariants, where `RegisterBackport` is absent.

## If your mod reads which version loaded

### Nothing breaks

`CurrentVariant`, `CurrentLocationId`, `IsManaged`, `DisplayName`, `Variants` and `VariantResolved` behave as
before. Two things are new in what they can return:

- a **source's id** (`myversion`) where you have only ever seen `original`, `backport` or a declared extra;
- more than one mod's version on the same map, so **`backport` no longer tells you whose map it is**: every
  mod's backport is `backport`.

### Key on `SelectedId` instead

```csharp
var selected = maps?.GetProperty("SelectedId")?.GetValue(null, null) as string;   // null before 1.2.0
if (selected == null)
    selected = /* CurrentVariant, as before */;
```

- `SelectedId` is `owner:variant`: the plugin GUID that registered the version and that mod's own word for
  it. The map SPT ships is `spt:shipped`.
- The part after the colon is short and readable, good for a file name or a map key. The whole id is
  unique across mods.
- Like `CurrentVariant`, it is **null until the version is decided**, and null for a map MapVariants does not
  manage.
- `IdFor(locationId, variant)` gives the same id for any entry of `Variants(locationId)`, to build a lookup
  before the raid.

Data you already stored under the old word (`backport`) stays valid for whoever wrote it; match it as a
fallback rather than migrating it.

## Callbacks: one reminder

A callback may run **more than once for one raid** (when the choice is applied, and again as the map loads).
Switch only when the word differs from what you hold, and never throw: a throw is logged and costs only your
own switch.

## Checklist

1. Registering, sharing a map with another mod: `RegisterSource` on both sides behind a 1.2.0 check, server
   data from `Shipped`, the same id on both sides, your id accepted wherever you read `CurrentVariant` or
   `InstalledVariant`, stand-down workarounds dropped only when both mods are sources.
2. Registering, no other mod on your map: nothing; your name is what the tile shows, so make it say what
   your version is.
3. Reading: `SelectedId` with `CurrentVariant` as the fallback; key on its variant part or the whole id;
   keep matching data stored under the old word.
4. Callbacks: idempotent and never throwing.
