# Cairn

A Valheim mod by [Raven Iron](https://github.com/RavenIron).

**Stack stones on a headland and they burn.**

On a world with no map, knowing where you are is knowledge — and it lives in players' heads
and in screenshots pasted into Discord. Cairn moves it into the world, where it can be built,
found, lit, and lost.

It adds no pieces, no assets, and nothing to your screen.

---

## Building a cairn

**Stack four stones.** `Placeable_Stone` is a vanilla Hoe piece costing one stone each, so a
cairn is four stone and a minute's work. Pile them within four metres of each other; spread
them out and it stays a scatter, because a cairn is narrow and a wall is wide, and that is how
the mod tells them apart.

**It lights itself.** Within about ten seconds a fire appears on the crown, and it can be seen
from hundreds of metres away.

**Name it, if you want.** Put a sign within six metres and write on it, and the place has a
name. A cairn with no sign is still a lit waymark — plenty of them should be.

Every one looks different because you built it, and uninstalling the mod leaves exactly what
it appears to be: a pile of rocks with a sign on it.

## The light

It **holds its size at distance** rather than shrinking away — a fire on a far headland reads
as a point of light the way a real one does, not as something that gets small until it is
gone.

And it is **hidden by terrain.** A ridge between you and a cairn puts it out, so you have to
move to see it. That is the difference between navigating and being told, and it is the reason
this is a beacon and not a marker.

Colour is yours: `BeaconColour` takes hex — `4A9EFF` for cold blue, `7CFF7C` for something
stranger. The default is firelight.

## The raven

Stand at a named cairn and Hugin may land and say its name.

He is fussy, and deliberately so — vanilla will not land the bird below 30 metres of altitude,
with anything hostile within ten metres, or during a world event. So he is silent exactly when
you would most want him, which is why nothing in the mod depends on him. He is a grace note,
not a compass. `cairn raven` will tell you why he has not come.

## What it does not do

No map. No compass. No waypoints, no markers, no pins, no arrows, no HUD, no readout of any
kind. If you learn where you are, you learn it by looking at the world.

It adds **no prefabs** — a cairn is *recognised*, never provided — ships **no assets**, and
leaves **vanilla recipes alone**.

## Installing

Install on the **server and the clients**. One DLL does all three roles: a dedicated server
sweeps the world and owns the ledger, a client draws the lights and carries the raven's line,
a listen host does both.

Requires [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/).

Landmarks are stored per world, beside the save, in a plain text file you can read.

## Configuration

Everything is in `BepInEx/config/com.raveniron.cairn.cfg`, and every value carries a note
explaining what it costs. The ones most worth knowing:

| | |
|---|---|
| `PileMinPieces` | how many stones make a cairn (4) — raise it if decorative stones start lighting up |
| `PileMaxExtentMeters` | the footprint that separates a waymark from a building (4m) |
| `LandmarkPairMeters` | how far a sign may stand from the cairn it names (6m) |
| `BeaconColour` | hex, `RRGGBB` |
| `BeaconMaxDistanceMeters` | how far a light carries (800m) |
| `BeaconOcclusion` | terrain hides beacons. Leave this on. |
| `LandmarkRotationSeconds` | how quickly a new cairn is noticed (20s) |
| `EnableRavenVoice` | the bird |

## Console

`cairn status` · `landmarks` · `beacons` · `raven` · `prefabs <text>` · `pieces <text>` · `save`

Each reports **state rather than verdicts** — `beacons` says whether a light is out of range
or hidden behind a hill, `raven` says which of four reasons the bird is quiet for. They exist
because guessing at each of those cost an afternoon.

## Compatibility

Nothing is patched that another mod is likely to want. Environment and weather are read-only,
so the season mods are untouched; no prefab is added, so no save is at risk; and nothing is
written to `Minimap`, so a map mod and this one simply ignore each other — though running both
does rather blunt the point.

---

**Design document** — the reasoning behind every decision, including the ones that were
wrong: <https://claude.ai/code/artifact/a04abbae-14d5-4a21-9bdc-032e91da0936>
