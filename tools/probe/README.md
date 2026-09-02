# Cairn Probe

Throwaway client-side BepInEx plugin. It exists to settle **one** question before any
Cairn code gets written: *can a client-drawn beacon column be seen at 400m, and what
erases it first?*

It patches nothing, writes nothing to the world, and touches no game state. Delete the
DLL when the measurement is done.

**This folder is research, not part of the mod.** Nothing in the plugin references it and
nothing packages it — it is built by hand when a measurement is needed, and it stays here
for the next rendering question that needs an answer.

## What it measures, and why it has to be measured

| Value | Why the decompile can't answer it |
|---|---|
| `RenderSettings.fogDensity`, `fogMode` | `EnvMan` blends density every frame from the current environment's four day-phase values — all serialized in prefabs, not in code |
| `Camera.main.farClipPlane`, `fieldOfView` | `GameCamera` only assigns the **near** plane in code; the far plane is serialized |
| `Camera.layerCullDistances` | Per-layer culling is the one mechanism that could silently kill a distant effect |
| `ZoneSystem.m_activeArea` / `m_activeDistantArea` | The code initializers read `1` and `1`, but the prefab overrides both — the decompile is actively misleading here |

Derived from those, per sample:

- **contrast** at 100/200/400/800m — the fraction of the beacon's contrast against fog
  that survives at that range. Under exponential-squared fog, density is the whole
  ballgame: `0.001` leaves ~85% at 400m, `0.01` leaves nothing.
- **horizon** — the range where contrast falls to 10% (configurable). This is the number
  that decides the design.
- **pixels tall** — how many vertical pixels a 40m column covers at each range, from the
  real FOV and screen height. `GameCamera.m_fov = 65` and Unity's `fieldOfView` is
  *vertical*, so at 1080p a 40m column at 400m is ~95px. Geometry is not the problem;
  fog might be.

## Build

```powershell
.\tools\fetch-libs.ps1      # from the repo root, once per machine
dotnet build .\tools\probe\
```

Shares the repo's `libs\` through a relative path — no hardcoded Steam or user path.
Override if this folder ever moves:

```powershell
dotnet build .\tools\probe\ -p:LibsDir=D:\path\to\libs
```

References only BepInEx and Unity. **No `assembly_valheim` reference** — every game member
is reached by reflection, so the probe can't be broken by the publicized-at-compile-time /
private-at-runtime trap, and an unresolvable member degrades to `?` or `-1` in the output
instead of throwing.

## Deploy

Client-side only. On a headless process it logs one line saying so and disarms.

Skadi's client runs through Gale, so the live plugin path is:

```
%APPDATA%\com.kesomannen.gale\valheim\profiles\<profile>\BepInEx\plugins\
```

Close Valheim first — it locks the DLL.

## Run protocol

1. Load any world and stand outside, in the open.
2. Let a full in-game day pass. Dawn and dusk blend the four per-phase densities, so the
   day cycle produces the curve for free.
3. Force the weather that matters, from the console with `devcommands` enabled:
   `env Clear`, `env Misty`, `env Rain`, `env ThunderStorm`, and whatever else the biome
   you care about actually rolls. (Forcing an environment by console is fine — the house
   rule forbids *patching* environment selection, not typing at it.)
4. Read `BepInEx\config\cairn-probe-fog.csv`, or grep the log for `[cairn]`.

Rows are emitted on change — new environment, fog mode flip, >5% density move, camera
cull change — plus a heartbeat every 5 minutes so a quiet probe is distinguishable from a
dead one.

## Reading the result

- **`horizon` ≥ 400m in Clear** — the beacon works as designed. Proceed.
- **`horizon` < 400m in Misty/Rain** — the beacon is short-range in bad weather. Probably
  *correct* behaviour rather than a defect (a beacon lost in fog is honest), but it has to
  be a deliberate decision, not a surprise.
- **`farClip` < 400** — fatal, and would change the whole approach. Very unlikely: vanilla
  renders mountains kilometres out.
- **`layerCull` anything but `none`** — the real culprit, and worth knowing which layer.
- **`zones near=N`** — informational. Confirms why the beacon must be drawn from synced
  state rather than from a loaded object: at 400m the anchor object isn't instantiated.

## What it does NOT answer

Whether a real particle column *reads* as a landmark — that needs the actual effect built
and looked at. This probe only rules out the things that would make building it pointless.
