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

**Status: PUBLISHED 2026-09-02/03, v0.7.0.** Live on the stores under team
`RavenIronStudios`, categorised "Client & Server - must be installed on both". Built,
verified and shipped in a single day.

Stacked stones become a cairn, a cairn burns, and the light was seen from 420m down a chain
of fifteen the owner built by hand.

Every task except the fog probe now has a live run behind it: the skeleton on all three
roles, the ledger with its sweep, sign pairing, prune, unlight, drift carryover and v1-to-v2
migration, and the beacon itself. Off-game 156/156, and every load-bearing assertion was
proven to fail without its fix.

The beacon is fully verified, occlusion included: a ridge really does put a light out. What
remains is **the raven** — the line is offered correctly but no bird has yet been seen — and
**task 0**, the fog measurement in
`tools\probe\`, still unrun and now genuinely optional: it was a gate when the beacon was a
grey plume, and a bright point at night is a different proposition. See **Build order**.

---

## Commands

```powershell
.\tools\fetch-libs.ps1              # once per machine: copies game/BepInEx DLLs into libs\
.\tools\run-tests.ps1               # off-game logic tests (net10) — run before every commit
dotnet build .\Cairn\Cairn.csproj   # the mod
dotnet build .\tools\probe\         # the fog probe (research only, never shipped)
```

To test in-game: copy `Cairn\bin\Debug\Cairn.dll` into `<Valheim>\BepInEx\plugins\`, load a
world, and type `cairn status`. The console is the only instrument this build has.

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

Built:

```
Cairn/                     plugin (net472) — role-aware single DLL
  Cairn.cs                 entry point; role detection, Harmony, system registration
  Config/ModConfig.cs      config surface; every system gets an on/off toggle
  Core/IWorldSystem.cs     the contract every system implements
  Core/CairnTick.cs        the ONLY Update in the mod; budgeted cursor + autosave
  Core/LandmarkKey.cs      a place, to the nearest metre
  Core/Landmark.cs         one named place; format/parse and the escaping
  Core/LandmarkStore.cs    the ledger in memory; write-behind dirty flag
  Core/SignReading.cs      is this sign a landmark, and what is it called
  Core/PileDetection.cs    is this stack of stone a cairn, or a wall
  Core/Persistence.cs      world-scoped store on disk; atomic, fail-safe
  Systems/LandmarkSystem.cs  the sweep: stone becomes cairns, signs name them
  Net/LandmarkSync.cs      server-to-client beacon push; absolute, broadcast, 5s
  Visuals/Beacon.cs        the light — client-drawn, gated on a real GPU
  Patches/Patch_PieceCost.cs the vanilla recipe override (off by default)
  Patches/Patch_Terminal.cs  the `cairn` console
tests/CoreTests/           net10 harness; compiles the REAL source against stubs
docs/design-doc.html       source of the published design document
tools/fetch-libs.ps1       populates libs\ from a local Valheim install
tools/run-tests.ps1        the harness, in one command
tools/probe/               the fog probe — research, never shipped, never referenced
libs/                      gitignored; populated by fetch-libs.ps1
```

Planned:

```
  Voice/HuginVoice.cs      optional Raven static texts                 task 4
  Systems/SoundSystem.cs   a tone carrying past visual range           task 5
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
| Anchor object | **A pile of stone marks it; a named `Sign` names it.** Amended 2026-09-02 by the owner, reversing "the sign is the anchor". A qualifying stack of vanilla stone pieces is a cairn and gets the light — that is the navigation function, and it works unnamed. A named sign within `LandmarkPairMeters` gives that cairn its name. A sign with no pile is a named place with no light; a pile with no sign is a lit waymark with no name. Both are legitimate and the ledger holds both. |
| New prefabs | **Still none.** The pile is DETECTED, never provided: a cairn is a pattern in what players build out of ordinary stone. Adding a piece would mean shipping a prefab, and a mod adding a prefab must ship server-side or `ZNetScene.CreateObjectsSorted` calls `DestroyZDO` on every hash it cannot resolve — silent damage in someone else's world. Everyone's cairn looks different, and with the mod uninstalled it degrades to exactly what it appears to be: a pile of rocks with a sign on it. |
| Vanilla recipes | **Untouched by default (0.4.2).** `StonePileStoneCost` briefly shipped at 10, rewriting vanilla stone_pile from 50 — the only place this mod reached outside its scope. `cairn pieces stone` then found `Placeable_Stone` at **[Hoe] Stone x1**, a single stackable stone, and the problem the override existed to solve stopped existing. Default is now 0: do not touch the game. The switch stays for anyone who prefers heaps to stacks. |
| A cairn is made of | **`Placeable_Stone`** (Hoe, 1 stone each) stacked, or `stone_pile` heaps. Four pieces minimum inside a 4m footprint. Architecture is deliberately excluded, so a stone HOUSE can never become a landmark. |
| The beacon | **A bright light on top of the pile** (owner, 2026-09-02), not a smoke column. Strictly easier than a plume, and it may make the fog measurement far less decisive: a bright point at night carries where grey smoke dies. |
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
- **THE RAVEN IN A WORLD MAY BE MUNIN, NOT HUGIN, AND THE FILTER IS SILENT.**
  `Raven.m_instance` is whichever of the two spawned, and `GetClosestStaticText` skips any
  text whose `m_munin` differs from the instance's `m_isMunin` — before distance, before
  priority, before anything. Hardcoding Hugin cost four wrong theories and most of an
  afternoon on 2026-09-02: the world's bird was MUNIN, so a correctly registered, correctly
  positioned line was discarded by a boolean, with every other signal reading healthy. Always
  match the live instance. `cairn raven` reports which bird it is.
- **`Sign` caps text at `m_characterLimit = 50`**, stores it in the ZDO with a revision
  counter, and captures the author as a `PlatformUserID`. Name length is not ours to
  choose.
- **A `spawn`ed sign has no structural support and destroys itself within seconds.** This
  is vanilla building integrity, not a mod interaction, and it burned a whole live test
  session on 2026-09-02: the sign was placed, the sweep ran, and `found=0` was reported —
  a completely honest answer about a world that no longer contained a sign. **To test the
  sweep, build a real supported structure and attach the sign to it** (`devcommands` then
  `nocost`, then build with the hammer so normal placement rules apply). Never conclude
  anything from a sweep over spawned pieces without first confirming they are still
  standing.
- **`Utils.GetMainCamera()` is a frame-cached `Camera.main`** — the main camera is tagged,
  so client-side code needs no game reference to reach it.
- **`Terminal.commands` is `public static` in the publicized assembly and PRIVATE at
  runtime — and try/catch does not save you.** Naming it directly in a Harmony postfix
  produced `FieldAccessException: Field 'Terminal:commands' is inaccessible` on the first
  dedicated-server run (2026-09-02), and the `try/catch` wrapped around that very line did
  nothing, because **Mono raises it when the METHOD IS COMPILED, not when the line runs**.
  The whole postfix aborted, taking `Terminal.Awake` and `Chat.Awake` with it, and the
  server shut down mid-boot. The build had reported 0 warnings. Two lessons, both
  load-bearing: reach private members only through reflection resolved at runtime, and
  never treat a try/catch as protection against an inaccessible member — the exception
  arrives too early to catch. `Patch_Terminal` now reads the map as a non-generic
  `IDictionary`, which names neither the field's type nor its generic arguments in our IL.
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

**1 — skeleton. DEDICATED-SERVER LEG VERIFIED 2026-09-02; client and listen host still
owed.** Plugin loads, `cairn` console registers and confirms itself by reading Terminal's
command map back, and the role line prints.

Verified on a minimal dedicated server (`C:\Users\donfr\ValheimServers\CairnTest`, port
2466, world `CairnTest`, Cairn.dll and nothing else), `isModded: True`, world created from
nothing:

```
Cairn v0.1.0 loaded — renderer=False, systems=0. Nothing is simulated yet: this is the skeleton.
cairn console registered — confirmed present in Terminal's command map (141 command(s) total).
Cairn online — role=dedicated server, authority=True, dedicated=True, renderer=False, systems=0, budget=2ms/frame
```

`dedicated=True` is the line that could only ever be settled by a server: `ZNet.IsDedicated()`
is a hardcoded `false` in the client assembly we compile against.

**The first attempt at this run killed the server outright** — see the `Terminal.commands`
entry in Known traps. It is the reason this task's acceptance is a run and not a build.

**Still owed:** a client run (`role=client, authority=False`, `cairn status` answering at
F5) and a listen host (`role=listen host, authority=True`).

**2 — the ledger. STORE BUILT AND TESTED 2026-09-02; THE SWEEP IS NOT WRITTEN.** Landmarks
live in a world-scoped sparse store: position (to the metre), name, author, first and last
seen. `LandmarkStore` holds it, `Persistence` writes it, `CairnTick` loads it and autosaves,
and `cairn landmarks` / `cairn save` read and flush it.

**Off-game: 93/93**, and all four load-bearing assertions were proven to fail without their
fix — header-by-content, the BOM, first-seen preservation, and culture-invariance. Every
acceptance criterion below is covered by a test EXCEPT the in-game half.

**The sweep is built too** (`Systems/LandmarkSystem.cs`, registered): it walks sign ZDOs
with `ZDOMan.GetAllZDOsWithPrefabIterative` — vanilla's own self-chunking traversal, one
whole prefab per tick — reads `ZDOVars.s_text` and `s_author`, and upserts. Pruning happens
only when a rotation completes CLEANLY: a sweep that threw halfway has not proved a
landmark is gone, and deleting on incomplete evidence is how a ledger quietly empties
itself. `SignReading` holds the accept/reject rule and is covered off-game.

**`SignPrefabs` = `"sign,sign_notext"` — VERIFIED IN-GAME 2026-09-02** by `cairn prefabs
sign` against a loaded `ZNetScene`: exactly two of 3458 prefabs contain "sign", and those
are their names.

**The stone pieces, from the same run** (`cairn prefabs stone`, 103 matches, buildable ones
picked out): `stone_pile`, `stone_wall_1x1`, `stone_wall_2x1`, `stone_wall_4x2`,
`stone_floor`, `stone_floor_2x2`, `stone_pillar`, `stone_arch`, `stone_stair`, and
`piece_stonecutter` as the stone workbench. `stone_pile` is named like exactly what a cairn
is made of and is the first candidate to check. **Which of these a player can actually
BUILD is a separate question the prefab list cannot answer** — the sweep's per-prefab
`found=` counts settle it empirically.

**The lucky-right-answer trap, worth keeping.** The retracted hash search had named `sign`
and `sign_notext` too. It was still a broken instrument — its control failed — and landing
on the right answer by luck is not evidence of anything. Had the retraction been skipped
because "it turned out to be right", the same method would have been trusted for the stone
names, where it had no chance at all.

**A retracted measurement, kept because the mistake is the lesson.** On 2026-09-02 these
names were "confirmed" by computing `GetStableHashCode` for each candidate and searching
real world saves for the 4-byte value: `sign` and `sign_notext` both appeared, the three
other guesses did not, and `piece_workbench` appeared as a control. It was wrong. Re-run
against a freshly saved test world, the SAME control returned **zero**, and no plain text
appeared in that file either — the save format defeats raw byte scanning, so the original
hits were never prefab data. The tell was there in the first run and was rationalised away:
`piece_workbench` returned 1 and 2 in worlds that certainly hold more. **A control that
disagrees with itself between two files has failed, whatever the other rows say.**

A wrong or missing prefab name costs a silent zero matches, which is why the first completed
rotation of every session logs its per-prefab counts unconditionally. **Read that line first
on any live run**, and note it distinguishes the two zeroes: `found=0` means no such objects
exist at all, while `found=N named=0` means the names are right and nothing is written on
them. The first version logged only the accepted count and could not tell those apart, which
cost a live session.

**VERIFIED IN-GAME 2026-09-02 on the dedicated server.** A supported sign named `test` was
built by a player, and the whole chain ran unassisted:

```
[LandmarkSystem] sweep complete (sign found=1 named=1, sign_notext found=0 named=0)
  — 1 landmark(s), 1 changed, 0 pruned
```

and one autosave later, on disk:

```
version	1
# x	y	z	firstSeenUtcTicks	lastSeenUtcTicks	author	name
10	49	27	639239624414309706	639239624414309706	Steam_76561198392625778	test
```

Closed by that single row: the sweep finds a real sign's ZDO; `ZDOVars.s_text` and
`s_author` read correctly; the author stored is the PLATFORM ID and not a display name, as
designed; the key is metre-rounded; firstSeen equals lastSeen on a first sighting; the file
is world-scoped by uid; `.bak` rotated (65 bytes, the previous header-only file) with no
`.tmp` orphaned; and the first bytes are `76 65 72` — "ver", **no BOM**, which is the
shipping writer behaving on a real world exactly as the harness asserts.

**VERIFIED LIVE 2026-09-02, the stone side too.** Five `stone_pile` pieces became one cairn; a pile paired with a sign keyed the landmark on the SIGN and kept its firstSeen; narrowing StonePrefabs unlit that cairn without erasing its name; and an unnamed pile persisted through a save as `52 42 18 ... unknown 1 51.69094 41.57542 18.4799442` with an EMPTY name field - the exact row the sparseness rule would have dropped before mutation testing caught it. The 10-stone recipe override applied and announced itself.

**Still owed:** the PRUNE path — break the sign, and a completed rotation should report
`1 pruned`. It is the half that deletes, so it is the half to trust least. Also: a store
surviving a server restart with values intact, two worlds producing two stores in the same
directory, and a corrupt file quarantining to `.corrupt` at error level.

**3 — the beacon. VERIFIED BY EYE 2026-09-02.** A light drawn client-side at each cairn's
crown from `LandmarkSync`'s broadcast, depending on no ZDO of the cairn's own — which is
what lets it work at range, since at range those are not loaded.

The acceptance was always "a player stands 400m away and *sees* it, and no log line
substitutes for that". Met on a chain of **fifteen lit cairns spanning ~420m**, built by
hand and sighted down its length: the distant beacons still read as points of light rather
than having shrunk away. **Constant angular size is the claim the whole design rests on** —
the glow is scaled BY RANGE so it holds its size on screen — and it is exactly the thing a
log could never have settled.

Confirmed on the client: `Beacon: using shader 'Sprites/Default'` — the same shader
Ragnarok's Wrath landed on, so the stripped-shader lesson transfers rather than merely
rhyming — and `Beacon: occlusion mask resolved (34817)`, layers 0, 11 and 15.

**OCCLUSION VERIFIED 2026-09-02.** `cairn beacons`, sighted down the trail:

```
  (162, 37, -9)  129m  vis=0.00  HIDDEN (terrain in the way)
  (255, 36, 11)  222m  vis=0.00  HIDDEN (terrain in the way)
  (275, 38, 13)  242m  vis=1.00  lit
  (324, 36, 20)  292m  vis=0.00  HIDDEN (terrain in the way)
  (43, 48, -8)    10m  vis=1.00  lit
```

The proof is that it DISCRIMINATES: 242m visible while 222m and 292m are hidden. A broken
raycast gives all-hidden or all-lit; this is the ragged pattern real ground produces. A
beacon behind a ridge goes dark, so the mod is not the waypoint marker house rule A forbids
— which was the one claim that would have quietly invalidated the whole design.

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
- **Prove a new test fails without its fix.** It caught one on the day it was written: the
  first culture test used `de-DE` and passed even with `Format` switched to
  `CurrentCulture`, because every field in the record is an integer and integers render
  identically there. It was decoration. It now runs under a hand-built culture whose
  negative sign is U+2212 rather than ASCII hyphen — constructed rather than picked from
  the OS, since which real locale uses which sign varies with the ICU version, and a test
  that depends on that fails on someone else's machine for the wrong reason.
- **A clean build proves nothing about member access.** Anything reaching into game
  internals needs one in-game run before it is done.
- **Verify game APIs by decompile rather than assuming** — `ilspycmd`, read the body.
- **Bump the version before every deployment, not just before every release.** Minor per
  completed task, patch for any rebuild that leaves this machine — including a DLL copied
  to the test server. On 2026-09-02 five different builds all announced `v0.1.0`, and a live
  diagnosis turned on knowing which one was running; the build was identified by spotting
  new wording in a log message, which is luck rather than instrumentation. The version is
  single-sourced from the csproj `Version` property and the C# constant is GENERATED from
  it (`GenerateVersionConst`), so the two cannot drift — RW checks that at package time,
  which is too late to help a test build.
- **Ask before changing anything in the locked-decisions table.**

One rule specific to this mod: **when a feature is hard to make legible, the answer is
usually to make it bigger or louder, never to draw it on the screen.** That escape hatch
is closed on purpose.
