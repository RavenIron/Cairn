# Changelog

## 0.7.0

First release. Built and verified in one day on a dedicated server, against cairns stacked
by hand rather than fixtures.

**Stack stones and they burn.** A tight pile of ordinary stone becomes a cairn; a cairn
carries a light you can steer by; a sign within six metres gives the place a name. The world
remembers where they are.

- **No new pieces.** A cairn is *detected*, never provided — four `Placeable_Stone` (Hoe, one
  stone each) inside a four-metre footprint. Everyone's looks different, and with the mod
  uninstalled it degrades to exactly what it appears to be.
- **No vanilla recipe touched** by default.
- **No HUD, no map, no markers.** Everything a player sees is an object standing in the world.
- **The light is occluded by terrain.** A ridge between you and a cairn puts it out — that is
  the difference between navigating and being told, and it is verified rather than asserted.
- **Constant angular size**, so a beacon reads as a point of light at 50m or 500m instead of
  shrinking away. Seen down a chain of fifteen spanning 420m.
- **Colour configurable** as hex.
- **Hugin — or Munin — speaks a landmark's name** when you stand at it. Deliberately flavour:
  vanilla will not land the bird below 30m of altitude, near a hostile, or during a world
  event, so it is silent exactly when you would most want it.
- **Server-authoritative.** One role-aware DLL: a headless server sweeps and owns the ledger,
  a client draws and speaks, a listen host does both.

Console: `cairn status | landmarks | beacons | raven | prefabs <text> | pieces <text> | save`.

Every command reports state rather than verdicts, and says *why* a thing is not happening —
which is what four wrong theories about a bird taught in a single afternoon.
