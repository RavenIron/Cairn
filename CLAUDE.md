# Cairn

A Valheim mod by **Raven Iron**. On a world with no map and no portals, knowing where you
are is knowledge, and knowledge can be built. Cairn turns places into things the world
acknowledges: a named stone, a lit beacon on a headland, a bird that speaks the name of
the ground you are standing on.

**Not** a map, a compass, a waypoint list, a marker, or a shared pin board. Those are the
same cut Ragnarok's Wrath already made. If a task seems to call for drawing the answer on
the screen, that is a signal to re-read this section, not to draw it.

The land-side twin of Undertow's seamarks, and the same philosophy: coordinates become
knowledge a crew carries in their heads.

Design document (the reasoning behind every decision here):
<https://claude.ai/code/artifact/a04abbae-14d5-4a21-9bdc-032e91da0936>

**Status: NOTHING BUILT.** This is a scope document, not a roadmap in progress. The only
code that exists is `tools\probe\` — a research instrument that answers the one question
the whole design hangs on. See **Build order** at the bottom.

---

## Commands

```powershell
.\tools\fetch-libs.ps1              # once per machine: copies game/BepInEx DLLs into libs\
dotnet build .\tools\probe\         # the fog probe (research only, never shipped)
```

To inspect a game member — signature, accessibility, default parameter values, or the
actual method body — decompile it. `dotnet tool install -g ilspycmd`, then:

```powershell
$m = "<Valheim>\valheim_Data\Managed"
ilspycmd -r $m $m\assembly_valheim.dll -t Raven
```

Read the body, do not infer it from the shape of the output. Three of the four navigation
channels in this design were settled by reading a body that contradicted a reasonable
assumption.

**Skadi's client runs through Gale**, not the Steam folder — the live plugin path is
`%APPDATA%\com.kesomannen.gale\valheim\profiles\<profile>\BepInEx\plugins\`, and the log
sits beside it. Valheim locks the DLL while running, so close the game before copying.

---

## Layout

```
Cairn/                     plugin (net472) — role-aware single DLL   [NOT YET CREATED]
  Config/ModConfig.cs      config surface; every system has an on/off toggle
  Core/                    LandmarkKey, LandmarkStore, Persistence, IWorldSystem, Tick
  Net/LandmarkSync.cs      server-to-client landmark push
  Visuals/Beacon.cs        client-side procedural column; gated on a real GPU
  Voice/HuginVoice.cs      optional Raven static texts
  Patches/                 Harmony patches (expected: Sign, Terminal)
tests/CoreTests/           net10 harness; compiles the REAL source against stubs
docs/design-doc.html       source of the published design document
tools/fetch-libs.ps1       populates libs\ from a local Valheim install
tools/probe/               the fog probe — research, never shipped, never referenced
libs/                      gitignored; populated by fetch-libs.ps1
```

---

## House style

**Inherited wholesale from Ragnarok's Wrath.** Same studio, same failures paid for once
already. In particular: prefixes for behaviour at `Priority.Low` honouring `__runOriginal`;
decorating postfixes at default priority where appending to a return value is the point;
**no long-lived coroutines** (a time-budgeted cursor driven from one `Update`); cosmetics
in their own try/catch off the gameplay path; **publicized assemblies are compile-time
only** — private members through a cached `AccessTools.MethodDelegate`, retried rather
than latched.

Two rules Cairn adds, because its subject matter invites exactly these mistakes:

**A. Every answer is an object in the world.** No overlay, no readout, no list, no
directional indicator, no screen-space anything. If a player learns where they are, they
learn it by looking at terrain, a stone, a fire, a bird, or the sky. The moment the mod
renders a bearing it has become the companion app that was deliberately cut.

**B. A landmark is player-authored, never mod-authored.** Cairn does not decide what is
worth remembering and does not seed the world with points of interest. It notices what
players build and gives it weight. A world where nobody has built anything has no
landmarks, and that is correct.

---

## Locked decisions — do not revisit without asking

| Decision | Answer |
|---|---|
| Scope | **Knowing where you are, and telling someone else.** Nothing else. |
| Map / compass / waypoints / markers / pin sharing | **None.** See house rule A. |
| Anchor object | **The vanilla `Sign`.** A named sign is a landmark. No new prefabs — keeps us clear of the `ZNetScene.CreateObjectsSorted` → `DestroyZDO` landmine, and a cairn still reads as a cairn with the mod uninstalled. |
| Beacon rendering | **Client-drawn from synced state, never a networked object.** RW's `ZoneSync` → `PlagueFog` path, already verified in-game. Rendering does not depend on the anchor's ZDO being loaded, which at beacon range it will not be. |
| Vanilla smoke | **Never used.** `SmokeSpawner.Spawn` refuses to emit past 64m from the local player, `Smoke` is globally capped at 100 puffs with `FadeMostDistant()` culling the furthest first, and every puff is a `Rigidbody`. The engine's own policy is the opposite of a beacon's. |
| Hugin / the Raven | **Optional flavour, additive only, never load-bearing.** Register static texts; never overwrite vanilla tutorial text; degrade to silence. It cannot be the navigation channel — see Known traps. |
| Seasons / weather / `EnvMan` | **Read-only consumption. Never patched.** RW's rule 4 applies unchanged; Seasonality and Seasons own that ground. |
| Terrain / materials / textures | **Never touched.** |
| Player data (kills, deaths, playtime) | **None.** The Raven's Call's job, a separate mod, no data connection. |
| Relationship to Undertow | Undertow owns **water motion and nothing else**. Cairn owns signals. Read-only bridge if any, same shape as `WrathBridge`. |
| Relationship to Ragnarok's Wrath | **Separate store.** RW's zone drift is what happened to land; Cairn's ledger is what people named. A landmark must never become a zone statistic. |
| Persistence | **World-scoped sparse file keyed by world uid**, RW's `Persistence` shape, `FileSource.Local` explicit. ZDO custom keys rejected for the same reason as RW: a landmark is a coordinate, not an object. |
| Seabirds as a landfall signal | **Not in v1.** Good idea, unproven, and it competes for the same "is this legible?" budget as the beacon. Revisit once the beacon is shipped. |
| Console prefix | `cairn` |
| GUID / namespace | `com.raveniron.cairn` / `RavenIron.Cairn` |
| Distribution | Thunderstore **and** Hexium, same zip, store team `RavenIronStudios`, packaged by `tools\package.ps1` — never hand-zipped. |
| Timeline | Open-ended. Done when it's done. |

### Deliberately unresolved

**Player-authored text on a shared server.** A named sign is user content, and Cairn gives
it reach it did not have before — a name that previously only a visitor read could be
spoken by the raven, or attached to a beacon visible across a valley. Vanilla already has
`m_isViewable` and a mute path in `Sign.GetHoverText`. **Decide before any name leaves the
sign it was written on**, and default to the conservative answer.

---

## Compatibility constraints

**Ragnarok's Wrath / FireFront / Undertow (Raven Iron)** — a beacon is a fire. FireFront
owns every consequence of a fire, as always; Cairn decides only that one is lit.

**Seasonality (RustyMods) / Seasons (shudnal)** — untouched by construction. Cairn drives
no visuals from season and patches no environment.

**AwayFromHome (Wubarrk)** — a beacon at an unattended site must not become a hazard, and
must not need a player present to *exist* in the ledger. Landmarks are static data; nothing
about them ticks.

**Any minimap or waypoint mod** — no conflict, because Cairn writes nothing to `Minimap`
and reads nothing from it. Players running one get a mod whose whole point is dulled. That
is their call, not ours.

---

## Known traps

Every fact below was read out of the shipping assembly by decompile on **2026-09-01**, not
inferred from the shape of an API.

- **`SmokeSpawner.Spawn` returns without emitting when the local player is more than 64m
  away.** Vanilla fire smoke does not exist at range — not culled, never created. Any
  design that reaches for smoke as a distance signal is dead at 64m.
- **`Smoke` is globally capped at 100 puffs** (`GetTotalSmoke() > 100` → `FadeOldest()`),
  shared by every fire in view, and `FadeMostDistant()` culls the furthest first. Adding
  smoke steals from everything else the player can see.
- **The raven cannot land below 30m altitude.** `Raven.FindSpawnPoint` requires
  `height > 30f`, `normal.y > 0.5f`, and `|height - playerY| < 2f` across 20 tries at
  10–15m from the camera forward. It is absent on exactly the coastlines a navigator cares
  about.
- **The raven is a 15m proximity trigger, not a beacon.** `GetClosestStaticText` searches
  `m_spawnDistance` = 15m, and only among `RavenText` entries with a live `GuidePoint`.
- **The raven goes silent when it matters most.** It despawns on `EnemyNearby`
  (`LootSpawner.IsMonsterInRange`, 10m) and on `RandEventSystem.InEvent()` — so RW's own
  Devastating Storms silence it.
- **`ZDO.Distant` is copied from the prefab's `ZNetView.m_distant`** and vanilla
  fireplaces are near-only, so past `ZoneSystem.m_activeArea` the anchor object is not
  instantiated on the client at all.
- **Distant ZDOs are sent last and starve.** `ZDOMan.CreateSyncList` appends the distant
  list only when `toSync.Count < 10`.
- **`ZoneSystem.m_activeArea` and `m_activeDistantArea` read `1` in the decompile and that
  is not the shipped value** — the prefab overrides both. This is a case where reading the
  body still gives the wrong answer; measure it in-game.
- **`RenderSettings.fogDensity` is re-blended every frame** from the current environment's
  four day-phase values (`EnvMan`, four `+=` lines). There is no single fog number to look
  up; it is a curve across the day.
- **`GameCamera.m_fov = 65` and Unity's `fieldOfView` is vertical.** At 1080p a 40m column
  at 400m is ~95px tall and ~7px wide at 3m thick. Height buys legibility linearly; width
  past ~8m is wasted. Build the beacon tall, not fat.
- **`Sign` caps text at `m_characterLimit = 50`**, stores it in the ZDO with a revision
  counter, and captures the author as a `PlatformUserID`. Name length is not ours to
  choose.
- **`Utils.GetMainCamera()` is a frame-cached `Camera.main`** — the main camera is tagged,
  so client-side code needs no game reference to reach it.
- Inherited from RW and still true here: a publicized member listed as `public` may be
  inaccessible at runtime; `InvariantCulture` on everything that touches disk; a cloud
  world has no filesystem path; setting a ZDO's position does not move an object;
  `ZRoutedRpc.Register` tops out at 6 type parameters and `ZNetView.Register` at 4.

---

## Build order

Each task ends with a measurement, not a build. Nothing is "done" on a clean compile.

**0 — the gate.** Run `tools\probe\` for one in-game day across Clear, Misty, Rain and
ThunderStorm. **Acceptance:** a fog horizon in the CSV for each. If Clear's 10% horizon
falls short of 400m, the beacon is not the primary channel and this document needs
rewriting before task 3 exists.

**1 — skeleton.** Plugin loads on client, dedicated server and listen host; `cairn` console
registered, confirmed by reading `Terminal.commands` back rather than assuming;
`dedicated=True` logged on the server binary.

**2 — the ledger.** Named signs become landmarks in a world-scoped sparse store: position,
name, author, first seen. **Acceptance:** two worlds produce two stores in the same
directory; a store survives a restart with values intact; `.bak` rotated, no `.tmp`
orphaned; a corrupt file quarantines to `.corrupt` and says so at error level.

**3 — the beacon.** Client-drawn column at synced landmark coordinates, lit state driven by
a real fire at the site. **Acceptance:** a player stands 400m away on a hillside and *sees*
it. That is the whole test, and no log line substitutes for it. Shader chosen is logged —
Valheim strips standard particle shaders and `Sprites/Default` is the first that ships.

**4 — the voice.** Hugin speaks a landmark's name within 15m, above 30m altitude, no
hostiles near, no event running. **Acceptance:** heard in-game, and vanilla tutorial text
still appears afterwards.

**5 — sound.** A distance-attenuated tone from a lit beacon, audible past visual range in
fog. **Acceptance:** heard past the measured fog horizon.

**Deferred:** seabirds, and anything that would make a landmark drift, decay, or score.

---

## Working agreement

Same as Ragnarok's Wrath, and for the same reasons:

- **Run the test script before every commit.** Add tests for anything with serialization,
  parsing, or math. The harness compiles the *shipping* source, never a copy.
- **At least one serialization test round-trips through the shipping writer.** Assert on
  the bytes the mod actually wrote — hand-built fixtures agreed with each other and
  disagreed with disk for as long as they existed.
- **Prove a new test fails without its fix.**
- **A clean build proves nothing about member access.** Anything reaching into game
  internals needs one in-game run before it is done.
- **Verify game APIs by decompile rather than assuming** — `ilspycmd`, read the body.
- **Ask before changing anything in the locked-decisions table.**

One rule specific to this mod: **when a feature is hard to make legible, the answer is
usually to make it bigger or louder, never to draw it on the screen.** That escape hatch
is closed on purpose.
