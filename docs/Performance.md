# Performance

How to find what takes a frame's time and the main thread's memory, and what was found and changed so far. The
numbers are from one big Invasion save (a base of 230 stockpiles and 76 lamps overrun: its crystal gone, 2 bricktrons
and 63 corruptrons left, 65 units in all) on the developer's machine, with autosave off so every run starts the same.
Only compare numbers measured the same way.

## Measuring

### In the game (F7 and DevTools)

With debug mode on, F7 opens the performance window. "Measure systems" (or `[Performance] MeasureAtStart`) puts a
timer on every `Update`, `FixedUpdate` and `LateUpdate` of the game and the mod, on every component the game's clock
updates (`fixed:`, `update:`, `late:`, `slow:`), and on a few heavy methods (`labor:`, `path:`, `knowledge:`,
`goals:`, `net:`, `lua:`, `ui:`). The time table shows per system:

| Column | Meaning |
|---|---|
| ms/call | one call's average time; the default order, so costly calls that run now and then come first |
| ms/frame | time per frame, all calls together; the "Sort" button orders by it |
| max ms | the longest frame |
| calls/frame | how often it runs per frame |

The same from DevTools (see [DevTools](DevTools.md)):

| Endpoint | What it gives |
|---|---|
| `GET /api/frames` | frame times (avg, 1% low, p50-p99), garbage collections and how the collector runs (`memory.gc`), the systems (`msPerCall`, `msPerFrame`; `sort=call`), the allocators, the last spikes |
| `GET /api/frames/phases` | each frame split into Unity's phases: fixed, update, before/at/after the main camera, present; "unmeasured" time in the systems table is found here |
| `GET /api/frames/loop` | Unity 6 only: each of Unity's own player loop steps timed (Update scripts, animation, canvases, renderers, physics, rendering, the time between frames), in a release player too and without the frame monitor's cost; the first call starts it |
| `GET /api/frames/markers` | Unity 6 only: Unity's own profiler markers (culling, drawing, shadows, skinning, animation, physics) per frame. A release player has only a few; the development player (`PlayerBuild.ProfilingLinux`, `Build/Profiling/Linux`) has all of them. One recorder per marker makes the game slower while it runs; `stop=true` takes them away |
| `GET /api/render` | renderers, lights, animators by culling mode, cameras, quality settings (shadow distance, frame rate cap) |
| `GET /api/ui/canvases` | the canvases that draw, grouped by path |
| `GET /api/ui/overheads` | the overhead icons by kind: how many show and how many of their canvases are on |
| `POST /api/heap/scan`, `GET /api/heap` | what fills the managed heap, per type, root and holder |

The frame monitor itself costs about 1.5 ms a frame in a big base (a timer around some 500 methods); it only runs
with debug mode on, while F7's "Measure systems" button is on, from the first time `/api/frames` asks (until
`POST /api/frames/stop`), or from the start with `[Performance] SpikeLog` or `MeasureAtStart`. Judge the frame rate without it: restart the game and use `/api/frames/loop` and `/api/frames/phases`,
which cost next to nothing.

### The benchmark

A fixed, busy base to compare builds and changes with, made only of the original game's things (its build menu's
blocks and structures, units, resources, recipes and tasks; nothing the mod adds), so the same save can be measured on
the original game too.

1. Build the save once: `POST /api/benchmark/build` (from any game; it starts a new sandbox world on Big Island and
   leaves the running game without saving). It picks the place near the home crystal with the least ground to move
   for a 120 x 80 platform and levels it as the world editor's plateau brush does (the columns' tops set to one
   height, the trees, plants, boulders and ore on it taken away). The platform is walled with stone (merlons, a tower
   per corner, a door per side, lantern wards along the inside) and gets a row of workshops and other structures, a
   row with every block of the build menu, 48 stockpiles filled with the game's resources and a 12 x 12 x 8 keep left
   as blueprints. Then 70 bricktrons (builders, knights, archers), loose stone, iron, fibre and clay to carry, every
   workshop on a loop, a harvest task in the forest west of the platform, a quarry east of it and the keep's
   builders. `enemies=N` adds a raid of N corruptrons. It runs 30 s and is saved as "CSP Benchmark".
   `GET /api/benchmark` follows it.
2. Run it: load the save (`CSP_AUTOLOAD="CSP Benchmark"`) and `POST /api/benchmark/run`, or start the game with
   `CSP_BENCHMARK=1` to run it once the save is in (`CSP_BENCHMARK=quit` closes the game afterwards). It sets normal
   speed, warms up 20 s, then measures four fixed camera views on the platform for 15 s each (overview,
   stockpiles, workshops, the keep): fps, 1% low, frame time percentiles, garbage collections and Unity's
   player loop steps. The report is `BepInEx/devtools/benchmarks/benchmark-<date>.json` and `GET /api/benchmark`.

Turn autosave off (`[Saving] AutosaveMinutes = 0`) where the benchmark is run often, so the save is never written
over by a later autosave of it, and keep the frame monitor off (`SpikeLog`), which costs time itself.

### With Unity's profiler

The systems table cannot see Unity's own work (rendering, animation, physics, UI batching). For that:
- **Port player:** `PlayerBuild.ProfilingLinux` builds a development player into `Build/Profiling/Linux` (see
  [Unity 6 port](Unity6Port.md)). Install into it, add BepInEx, start it, and pick it in the editor's Profiler window
  (target list). This is the closest to what a player runs.
- **Editor (Unity 6 reconstruction):** play mode with the Profiler window. The editor adds 30-40 ms a frame of its own
  (EditorLoop, the profiler), slows down to about 1 frame a second when it is not focused, and Deep Profile reloads
  the scripts in the middle of play mode (BepInEx is gone then: stop and start Play again). Use the editor to find
  what is slow, never to judge how fast the game is.

Measure for 10-15 seconds once the save has loaded and the first garbage collection has passed: the first seconds of
a game are full of one-off work (pools filling, menus being built).

## Frame rate work, 29-10-2026 and 30-10-2026

| Step | Port player | Editor |
|---|---:|---:|
| Start | 73 fps (13.6 ms) | 9 fps (105 ms) |
| AI ticks per frame capped | | 19 fps (52 ms) |
| Decoration animators, test scripts, sound objects | 103 fps (9.7 ms) | |
| Hidden overhead icons' canvases off | 125 fps (8.0 ms) | |
| Frame monitor off (it ran from the start: `SpikeLog`) | 180 fps (5.5 ms) | |
| Ore mine search share, stockpile icons' animators off | 199 fps (5.0 ms) | |
| Units standing still skip the motion pass | about 205 fps (4.8-4.9 ms) | |
| Faction vision at 60 steps a second | 216 fps (4.6 ms) | |

The 1% low went from 43 to 64 fps in the port player. All changes are part of `FrameSavers` unless named.

### What was changed

- **AI ticks per frame (`AiTickCap`):** the game runs its AI (behaviour trees of units and projects, the enemy watch,
  animation tracking) in the fixed update, 50 times a game second. Below 50 fps Unity runs several fixed updates per
  frame to catch up, at 10 fps five, so the AI did five times the work in each slow frame and made it slower still.
  The AI now runs in at most `[Performance] AiTicksPerFrame` (2) of a frame's fixed updates, times the game speed.
  Physics and movement run in all of them. Below 25 fps units decide a little later in game time.
- **Animation tracking (`AnimatorTrackerCache`):** a unit's AnimatorTracker follows about 66 animation states and
  asked the animator for its state once per state every fixed update (some 30 000 calls into Unity a frame). It now
  reads each layer once per update.
- **Decoration animators:** some 640 animators on things that only show something (stockpile planks and logs, beacon
  handles, furnace fires, ward lanterns) animated every frame, also off the screen. They are set to `CullCompletely`
  and stop while none of their renderers is visible. The main camera's time fell from 1.0 to 0.2 ms.
- **Overhead icon test scripts:** every overhead icon (some 300) carried two of the developers' test components
  (`TestContainer`, `TestOrderQueue`) that Unity called every frame and that do nothing in the game. They switch
  themselves off.
- **Sound objects:** each of some 300 `AkGameObj` read its transform's position, forward and up up to a dozen times a
  frame to tell Wwise where it is. It reads position and rotation once now.
- **Hidden overhead icons' canvases:** a hidden icon is only made see-through by the game. Its own canvases were
  already switched off where they hold nothing but the hidden part; now the canvases nested in it are too (the
  content's, each line's arrow). A stockpile's hidden icon had three canvases on, about 700 in the save, that Unity
  sorted and batched every frame; now 230.
- **Ore mine:** the search for ore to dig takes 2% of a frame's time per mine (at most 0.3 ms), looks only at the part
  of each row inside the mine's sphere (about half the box) and rests 5 s between passes (a big mine's passes ran back
  to back). 0.43 ms a frame became 0.16 ms.
- **Stockpile icons' animators:** each stockpile's overhead icon (some 300) has an animator whose Show and Hide
  triggers nothing sets; it sits in a state without a clip. Unity still updated all of them every frame; they are
  switched off. Animation fell from 0.54 to 0.32 ms a frame.
- **Units standing still (`IdleUnitMotion`):** `Locomotion4` applied each unit's animator motion every frame: a
  helper transform moved, the facing levelled, the floor height looked up (route graph and surface lookups), rotation
  and position set, also for units standing still. When the last full pass changed nothing, the floor was exactly at
  the unit's feet and it has not moved, the pass is skipped for up to 0.25 s. About 0.2 ms a frame for 65 units.
- **Faction vision (`VisionRate`):** who sees whom is refreshed one small step per faction every frame, so the work
  per second grew with the frame rate. The steps now go at most at the rate of 60 frames a second.
- **Tunnel tasks (`TunnelGoalRefresh`):** a tunnel task looked over every voxel of its tunnel every 2 game seconds
  for blocks to dig and stairs to build in one go, up to 5.3 ms in one frame. The pass is spread over the next ticks,
  at most 0.25 ms a tick; the worst frame of a tunnel task fell to 1 ms.
- **Reach regions (`KnowledgeRegionsSpread`):** every 4 game seconds the game works out what each part of the map can
  reach. Its last step (2.3 ms) and the copy into the workers' knowledge (1.4 ms) ran in one tick; the copy now runs
  in the next fixed update, and the first step takes 40 roots a tick instead of 100 (0.9 to 0.4 ms). Worst frame 3.8 to
  2.4 ms.
- **Cleanup tasks (`GameLeakFixes`):** a cleanup task kept a list of every piece of litter ever picked up in its
  circle and went through it all on every refresh (every 0.6 s); it was never emptied. It is emptied after use.
- **Spike log off by default (`[Performance] SpikeLog`):** with debug mode on it started the frame monitor at once,
  about 1.3 ms a frame for everyone playing with debug mode. Config files made before keep their value.
- **Minimap:** columns that changed are drawn again every 2 s and the whole map once a minute, instead of the whole
  map every 15 s (about 4 ms a frame most of the time on a big map).
- **Shadows (`RenderTuning`):** the pass over the shadow casters is skipped while the camera stands still.
- **Route searches:** every slice of a search walked the map's corner column (about 512 node lookups) looking for the
  unit's own node in a list the game never fills; it is skipped.

### What was looked at and left

- **Route searches on a worker thread:** the search graph could be read from another thread, but the two tests a
  search makes on every node (target reached, can this voxel be crossed) call Unity (`transform`, `GetComponent`),
  which only works on the main thread. It would take snapshots of targets and crossings per search and thread-safe
  versions of several patches (500+ lines) for at most about 1.5 ms a frame. Not done.
- **Culling the units' animators completely off the screen:** the game waits on their animation states (a dig or a
  strike ends when its state ends), so stopped animators would leave workers waiting. Units cull only their bones
  (`CullUpdateTransforms`).
- **UNet:** in the editor `NetworkCallbacks.LateUpdate` cost 4.5 ms a frame; in the port player it costs nothing
  measurable (`net: UpdateServerObjects`). It was the editor.
- **Lights:** 76 point lights in view; after the changes above the main camera takes 0.2 ms on the main thread, so
  there was nothing to gain.
- **Incremental garbage collection:** on in Unity 6. It does not shorten the pauses of a heap this size (see below)
  and puts a write barrier on every reference store; worth a test with it off.

### What is left

- **The game's AI (fixed update), about 0.4 ms a frame on average:** the tasks' goal searches are already spread over
  ticks by the game. What still shows as single slow frames: a worker's work search (`fixed: Labor`, up to 4-5 ms when
  a frame runs two AI ticks after a slow one; `BestRecepteurToStore` about 0.75 ms a call, cached) and the reach
  regions' last step (`ResultStep`, 2.3 ms in one tick every 4 s). The monitor's `ai:` rows measure inside them.
- **Frame rate cap:** the game's graphics setting caps the frame rate (`Application.targetFrameRate`, 240 in the
  last measurements).
- **Update scripts, about 2.0 ms:** spread over many systems; the biggest are `Locomotion4` (about 0.3 ms for 65
  units, some 5 microseconds each: the grounded check, the state's update, the layer weights, a dozen small calls
  into Unity; indexing the state machine's handlers by signal was tried and made no measurable difference), the overhead icons' small updates (some 1000 calls,
  about 0.3 ms together). The frame monitor measures inside `Locomotion4` (`loco:` rows); its ten or so timers per unit
  add about 0.3 ms there themselves.
- **Rendering:** about 0.6 ms on the main thread in these measurements, where the camera looked at little; a view over
  the base costs more.
- **Garbage collection pauses:** about 80 ms in the port player (0.3-2.3 s in the editor) with 1.1 GB of managed heap.
  Unity's collector does not move objects; every collection reads the whole heap. The known big holders are the
  terrain's colour buffers (`Color32[]` in pools, about 250 MB). See also [Lua memory leaks](LuaMemoryLeaks.md).

## Invasion raids without a crystal

While measuring, the frame rate fell to a tenth over a few runs: the save had grown from 9 to some 1000 enemy raids,
each with its beacon, timers and overhead. With the players' crystal gone, the gamemode's wave spawn made its raid and
then threw on the missing crystal (`invasion.lua`: attempt to index a nil value) before the next wave's timer started,
so it ran again on every slow update, and the autosave kept them. `GameLeakFixes` now skips the wave without a crystal.
A save that already holds such raids keeps them.

## Memory

The heap scan (`POST /api/heap/scan`) walks everything reachable from static fields and live components, a few ms per
frame, and reports MB per type, per root and per holding field. In the save above (29-10-2026) the reachable heap grew
from 810 to 870 MB in 11 minutes, with no single leak; Mono reported about 1450 MB in use after a collection, the rest
being gaps the non-moving collector cannot close. What was fixed before is in [Lua memory leaks](LuaMemoryLeaks.md)
and the `GameLeakFixes` entries of the [changelog](../CHANGELOG.md).
