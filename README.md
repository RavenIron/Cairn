# Cairn

A Valheim mod by [Raven Iron](https://github.com/RavenIron).

On a world with no map and no portals, knowing where you are is knowledge. Cairn turns
places into things the world acknowledges — a named stone, a lit beacon on a headland, a
bird that speaks the name of the ground you are standing on — and never once draws any of
it on the screen.

**Design document:** <https://claude.ai/code/artifact/a04abbae-14d5-4a21-9bdc-032e91da0936>

---

## Status: not built

This repository currently holds a scope, not a mod. `CLAUDE.md` carries the locked
decisions and the build order; `tools\probe\` holds a throwaway research plugin that
measures the one thing that can still invalidate the design.

Three of the four candidate navigation channels were settled by decompiling the shipping
game assembly rather than by argument:

| Channel | Verdict | Why |
|---|---|---|
| Smoke | **Rejected** | `SmokeSpawner.Spawn` emits nothing past 64m from the local player — not culled, never created |
| The raven | **Demoted to flavour** | Cannot land below 30m altitude, is a 15m proximity trigger, and goes silent during any world event |
| The beacon | **The design** | But drawn client-side from synced state; at range the anchor object is never instantiated |
| Fog | **Unmeasured** | `RenderSettings.fogDensity` is blended per frame from prefab-serialized values. This is task 0. |

## Building the probe

```powershell
.\tools\fetch-libs.ps1              # once per machine
dotnet build .\tools\probe\
```

Then drop `tools\probe\bin\Debug\CairnProbe.dll` into a client BepInEx `plugins\` folder
and read `BepInEx\config\cairn-probe-fog.csv`. See `tools\probe\README.md` for the run
protocol and how to read the result.
