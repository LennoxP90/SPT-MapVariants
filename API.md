# MapVariants for mod authors

MapVariants lets a player choose, per raid, which version of a map loads. Any mod can offer a version of
a map, and any mod can read which version loaded. This guide covers extension API **3.3**.

Everything is reflection-friendly: call it after a `BepInDependency` on `com.lennoxp90.mapvariants`, with
no compile-time reference. `Maps.ApiVersion` is the contract version: minor for an addition, major for
anything a consumer could break on. Nothing in 3.0, 3.1 or 3.2 has changed, so a mod written against any
of them keeps working.

## Which part do I need?

| Your mod | Use | Since |
|---|---|---|
| Replaces a map's scenes and data itself, and knows nothing of MapVariants | A `maps.json` entry, no code | 3.0 |
| As above, but wants to add its own entry without editing `maps.json` | `RegisterBackport` (client only) | 3.3 |
| Owns a backport and wants to switch it on and off itself | `Register` (client) + `Providers.Register` (server) | 3.0 |
| As above, plus further versions built on that backport | `RegisterVariants` + `Providers.DeclareVariant` | 3.1 |
| Offers its own version of a map that other mods may also offer one of | `RegisterSource` (both sides) | 3.2 |
| Only needs to know which version loaded (a map, an overlay, a tracker) | `SelectedId`, `SelectedOwner`, `SelectedName` | 3.3 |

A new mod that offers a version of a map should use `RegisterSource`: it never conflicts with another mod
on the same map. The older calls stay supported.

## Reading which version loaded (3.3)

Client, `MapVariants.Client.Api.Maps`:

| Member | Example | Meaning |
|---|---|---|
| `SelectedId` | `com.manimal.lighthouse:manimal` | Whose version this raid loaded, as `owner:variant`. The map SPT ships is `spt:shipped`. |
| `SelectedOwner` | `com.example.mymap` | The plugin GUID of the mod that registered the version, or `spt`. |
| `SelectedName` | `Woods - My Version` | The label the player saw on the tile. |
| `IdFor(locationId, variant)` | | The same id for any variant of any map, for example each of `Variants(locationId)`, so a lookup can be built before a raid. |

All of them are **null until the version is decided**, and for a map MapVariants does not manage. Key your
own data on `SelectedId`, or on its part after the colon when you need something short: that part is the
registering mod's own word for its version (`manimal`, `classic`, `halloween2026`), where `CurrentVariant`
says `backport` for every mod's backport.

How the owner is known: MapVariants reads it off the callback each mod already registers (the plugin whose
DLL that code lives in), and a `maps.json` entry's owner is its `pluginGuid`. No mod passes anything new,
so mods on the older API get a real owner too. A callback that is not in any loaded plugin's DLL gives the
owner `unknown`.

The variant part is the source's or extra's id as registered, or for a backport its variant name in lower
case with every other character run turned into a dash (`Manimal` -> `manimal`, `Halloween 2026` ->
`halloween-2026`).

```csharp
// Reflection, after a soft BepInDependency on com.lennoxp90.mapvariants
var maps = AppDomain.CurrentDomain.GetAssemblies()
    .Select(a => a.GetType("MapVariants.Client.Api.Maps", false)).FirstOrDefault(t => t != null);
var selected = maps?.GetProperty("SelectedId")?.GetValue(null, null) as string;   // null before 3.3 too
```

The older readers are unchanged:

| Member | Meaning |
|---|---|
| `CurrentVariant` | The wire word for this raid: `original`, `backport`, a declared id such as `halloween2026`, or a source's id. **Null means "not decided yet"**, never `original`. |
| `CurrentLocationId` | The location that answer is for. |
| `Decided` | Whether `CurrentVariant` is known yet. |
| `IsBackport` | True for every variant except `original`. |
| `IsVariant(id)` | Whether this raid is that exact variant. |
| `Variants(locationId)` | Every variant the map offers: `original`, the backport and its extras, then sources. |
| `IsManaged(locationId)` | Whether MapVariants manages that location. |
| `DisplayName(locationId, variant)` | The label a player sees, such as `Factory - Classic`. |
| `VariantResolved` | `Action<string>` event, raised with the variant once it is known, and again if it changes. |

## Offering a version of a map: sources (3.2)

A map can be offered by one backport (a `maps.json` entry or `Register`) and by any number of **sources**
at once, each owning one version under its own id. A map with one backport and two sources shows four
tiles: Vanilla, the backport and each source. A source can also stand alone, with no backport on its map.

**Ids.** A source's id is its wire word, such as `myversion`: lower case, not `original`, `backport` or
`unmanaged`, and only one mod may use it on a map. Register the same id on both sides.

**Server**, `MapVariants.Server.Variants.Providers`, resolved from the DI container by type name. At
`PostLoad`, build your version from `Shipped(locationId)`, never from the live location, which may already
hold another mod's backport. The objects inside are SPT's own: copy anything before changing it. Then call
`RegisterSource(locationId, variantId, location)`. Sources are installed at MapVariants' discovery
(`PostLoad + 500000`), so register before that.

```csharp
var shipped = providers.Shipped("woods");
var mine = new Location
{
    Base = cloner.Clone(shipped.Base),          // a new Base: what tells your version apart
    LooseLoot = myLooseLoot,                    // anything you replace
    StaticLoot = shipped.StaticLoot,            // anything you keep
    // ...the remaining fields, yours or shipped's
};
providers.RegisterSource("woods", "myversion", mine);
```

**Client.** From `Awake`, after a hard `BepInDependency`:

```csharp
Maps.RegisterSource(new[] { "woods" }, "Woods", "myversion", "My Version", "Vanilla",
                    variantTile: null, originalTile: null, onVariant: OnVariant, warnings: null);
```

`displayName` and `originalName` are used only when your call is the first to offer the map. `variantName`
is your version's name alone ("My Version", never "Woods - My Version"). `warnings` may be null; when it is
not, its lines show on your tile only, while it is selected. False means refused, and the log says why.

**Every client and the headless** need the same sources installed: an id a client has never heard of reads
as `original` there.

## The callback

Every registration hands MapVariants an `Action<string>`, and every registered mod hears every choice on
its map in its own two words:

- `backport` (or the extra's id, for `RegisterVariants`) when the choice is yours;
- `original` for anything else: the shipped map and every other mod's version alike.

So a source written against the two-word vocabulary keeps working however many other mods share its map,
and stands down when another is picked. The callback:

- runs **before the map's preset loads**, on the main thread;
- may run **more than once for one raid** (when the choice is applied and again on the scene load), so
  make it idempotent: switch only when the word differs from what you hold;
- must not throw or block. A throw is logged and costs only your own switch.

## Checking whose backport the server serves (3.3)

The word `backport` cannot say which mod is behind it. With a mod installed on the server only, the server can
serve one mod's version as `backport` while a client loads another mod's scenes as `backport`: both sides agree
on the word, and the raid runs one map's scenes over the other's data (containers with no loot, exits that do
not match), with no error anywhere. The check is opt-in; a mod that declares nothing is never compared.

- **Server.** A backport's server mod calls `Providers.DeclarePlugin(locationId, clientPluginGuid)`, or the
  four-argument `Providers.Register(locationId, original, backport, clientPluginGuid)`, with the GUID of its own
  client plugin. Any time during the load works.
- **Wire.** `/mapvariants/variant`, `/mapvariants/transitclaim` and `/mapvariants/hostvariant` answer with
  `plugin` beside `variant`: the declared GUID while the backport (or a variant built on it) is served,
  otherwise null. Older clients ignore it.
- **Client.** Every answer is checked against the client's map. It is an error when the server serves a variant
  no plugin here offers, a backport on a map with no backport here, or a backport whose `plugin` differs from
  the client's `maps.json` `pluginGuid`. On the map screen, the only point where both sides can still move, a
  choice the server answers with another mod's backport falls back to the shipped map on both sides. A headless
  or a joining client cannot change what the server already serves, so it logs the error and loads as before.
- **Undeclared.** With no `plugin` from the server, or no `pluginGuid` on the client, nothing is compared.

A source needs none of this: its id is its own, and an id a client does not offer is already reported.

## What the player sees

- One tile per version. Past four, the window shows pages of four, turned with chevrons, Q/E, Page Up/Down
  or the dots; the dot of the page holding the selection takes the name-tape colour.
- Tile images: `tiles/<locationId>-<variant>.png` beside MapVariants' client DLL, or the path you pass.
- F12 `DefaultSelection` starts as Vanilla on every map; the player opts into any other version.
- When nobody chose (a raid the headless hosts), the server rests on the backport, or on a map with no
  backport, on the source whose id sorts first.

## Older calls

**`maps.json`** (3.0), beside the client DLL; the server needs nothing, it notices a location whose data a
mod replaced at load:

    { "locationIds": ["lighthouse"], "pluginGuid": "com.manimal.lighthouse",
      "displayName": "Lighthouse", "backportName": "Manimal", "originalName": "Vanilla" }

**`RegisterBackport(locationIds, pluginGuid, displayName, backportName, originalName, standDown, backportTile,
originalTile)`** (3.3), client, from `Awake`: the same entry made at runtime, through the same parser, so a
backport plugin can offer itself without writing `maps.json` or asking for a restart. True also when the map is
already offered for that plugin. The server needs nothing, as for `maps.json`.

**`Register`** (3.0) and **`RegisterVariants`** (3.1), client; **`Providers.Register(locationId, original,
backport)`** and **`Providers.DeclareVariant(id)`** (3.1), server. A declared variant serves the backport's
server data and keeps the backport's client patches; the mod adds its own content on top (FactoryClassic's
`halloween2026`). One backport per map: a second `Register` on a map that already has one is refused. Move
to `RegisterSource` to share a map.

`Providers.InstalledVariant(locationId)` is the wire word the server's table is serving right now.

## Limits

- A mod that edits the location after your source registered (a spawn mod later in `PostLoad`) edits the
  shipped map, not your source. MapVariants replays what it changed onto your source each time it is
  installed, with the same rules as onto the shipped map: scalars, same-named exits and same-index airdrop
  entries cross, filled collections do not. Values your source set itself are overwritten only where such a
  mod changed the same value.
- A mod that only replaces a field of a sourced location at `PostLoad` is taken for a backport; discovery
  logs a warning when that happens beside a source.
