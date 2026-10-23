<p align="center"><img src="CastleStoryPlus/Assets/logo.png" alt="Castle Story Plus" width="480"></p>

<p align="center">
  <a href="https://github.com/Tricky12321/CastleStoryPlus/releases/latest">Download</a> ·
  <a href="CHANGELOG.md">Changelog</a> ·
  <a href="docs/Gallery.md">Gallery</a> ·
  <a href="https://github.com/Tricky12321/CastleStoryPlus/issues">Report a bug</a>
</p>

**Castle Story Plus** is a quality-of-life and gameplay mod for [Castle Story](https://store.steampowered.com/app/227860/Castle_Story/). It makes workers smarter, gives you more control over your army, adds new buildings, blocks, resources and research, and smooths out map loading and the UI. It is a [BepInEx 5](https://github.com/BepInEx/BepInEx) plugin that uses [HarmonyX](https://github.com/BepInEx/HarmonyX) to patch the game in memory at runtime, and every feature can be switched off on its own.

- **No game files are changed.** To uninstall, delete the plugin folder.
- **No game code is in this repository.** It contains only the mod's own code. Castle Story is © Sauropod Studio.
- **Saves stay compatible.** Saves made with the mod can still be loaded by the unmodded game.

> **Status:** every feature below has been ported from the earlier decompiled-game prototype to this plugin and loads without errors. They still need play-testing in the plugin form.

## Contents

- [Features](#features)
  - [Worker AI](#worker-ai) · [Economy and resources](#economy-and-resources) · [Mining and metallurgy](#mining-and-metallurgy) · [Nature](#nature)
  - [Building](#building) · [Storage](#storage) · [Workshops, market and research](#workshops-market-and-research)
  - [Army and combat](#army-and-combat) · [Workers and experience](#workers-and-experience) · [Pathfinding](#pathfinding)
  - [UI](#ui) · [Menus, saving and loading](#menus-saving-and-loading) · [Performance](#performance) · [Debug tools](#debug-tools)
- [Installation](#installation) · [Updates](#updates) · [Manual installation](#manual-installation)
- [Configuration](#configuration)
- [Reporting bugs](#reporting-bugs) · [Roadmap](#roadmap)
- [Building from source](#building-from-source) · [Project layout](#project-layout) · [How it works](#how-it-works)
- [Multiplayer](#multiplayer) · [Disclaimer](#disclaimer)

## Features

Each feature has a switch in the `[Features]` section of the config file (see [Configuration](#configuration)). Settings in other sections are named next to the feature, e.g. `[Economy] EnergyMultiplier`. See [CHANGELOG.md](CHANGELOG.md) for every detail, and the [gallery](docs/Gallery.md) for pictures of every building, block, armour stand and stockpile.

### Worker AI

Workers find, share and finish their work on their own.

| Feature | Switch |
|---|---|
| Workers start work on their own, pick up their next task faster, and help other task groups when theirs is done. | `WorkerAI` |
| Workers start their next task right away, fetch materials from the stockpile that makes the shortest trip, and store a full bag straight after digging, felling or harvesting. | `TaskFlow` |
| A task can be paused instead of deleted: no bricktron works on it until it is resumed. | `PauseTasks` |
| Limit how many bricktrons work on a task at a time, e.g. a quarry with 4. | `WorkLimit` |
| Give a single blueprint (e.g. a warehouse) top priority from the context wheel over it. | `BuildPriority` |
| Cleanup and repair round the base are two fixed tasks with their own priority and crew, instead of chores for every idle worker. | `BaseJobs` |
| Tasks and things workers cannot walk to (blueprints, dig voxels, harvest, repairs, loose items) are shown in red and skipped until something changes within 3 blocks of them. | `UnreachableTasks` |
| "Can't reach my destination" says what the worker could not reach and how far ("Log, 12 blocks away, 3 up"); host and single player. | `UnreachableThought` |
| **New:** a bricktron shut in a hole (a pit dug too deep, a closed-off pocket) digs its own way out to the nearest place on its team's routes (host only; uses the route map of `UnreachableTasks`). | `StuckEscape` |
| No duplicate build jobs; workers carry a full armful for a blueprint and the ones near it and deliver to them on one trip; builders also build blueprints that cost nothing. | `BuildJobs` |
| Faster task search in big task areas (mining, digging, many trees): no idle seconds after each block. | `FasterTaskSearch` |
| Task reservation: a worker reserves the task it chooses, so idle workers no longer walk to the same task together. | `TaskReservation` |
| Resource reservation: a worker reserves the items it fetches (a loose item, or its share of a stockpile), so others fetch elsewhere. | `ResourceReservation` |
| A worker fetching from a stockpile takes any item of the kind there, instead of failing when the item the game picked was taken first. | `StockpilePickup` |
| Direct task: select bricktrons and right-click a blueprint, tree or block of an area to have them do that task now. | `DirectTask` |
| The pick up order on a loose item: each selected bricktron takes an item of that kind no other one is after (not all the same one), then picks up more of that kind lying round it until its hands are full. | `PickUpAround` |
| Idle workers pick up loose items near the base and store them, no cleanup zone needed; items on top of a building are taken from its foot. The range is the home crystal's blue ring. | `AutoCleanup` |
| Idle builders repair damaged blocks near the base, no repair zone needed. The range is the home crystal's green ring. | `AutoRepair` |

The ranges of `AutoCleanup` and `AutoRepair` are set on the home crystal's task; "Show ranges" / "Hide ranges" on the crystal's context wheel shows or hides the rings.

### Economy and resources

More energy, more resources, and control over how much is dug.

| Feature | Switch |
|---|---|
| New workers arrive faster: brewed fireflies carry more energy (default +30%, `[Economy] EnergyMultiplier`). | `Economy` |
| Quarry limits per resource: a quarry digs stone, iron, brimstone, coal and blue crystal only until the team has the set amount of each (small + and - over each resource). | `QuarryLimit` |
| Quarry depth can be changed at any time, also while it is being dug, and quarries go 3 times as deep. | `QuarryDepth` |
| Dark crystals: slain enemies drop them (5 at 1 in 3, biftrons 15 at 1 in 2); the upgrades' crystal tier and giant bricktrons cost dark crystals. | `DarkCrystals` |
| Blue crystal veins in the deep rock of every map, so blue crystal can be mined. | `BlueCrystalDeposits` |

### Mining and metallurgy

Coal, steel and new ways to get at the ore under the ground.

| Feature | Switch |
|---|---|
| Coal and steel: coal is mined from veins in the deep rock of every map or burnt from logs in the furnace; the forge makes steel from iron and coal; the furnace burns coal as well as orange crystal (coal first); the upgrades' steel tier costs steel. | `Metallurgy` |
| **New:** mine shafts, 2 to 4 big lodes of orange crystal, iron, blue crystal and coal (no stone) in the deep rock of every map, showing on the cliff face, whose blocks take 3 times as long to dig; old maps and saves get them too. | `MineShafts` |
| **New:** "Ore mine" in the new task menu (harvest tasks, after Brimstone) is placed like a harvest task, and its bricktrons dig only the ore they can safely get at, following a vein into the rock, without digging stone. | `OreMine` |

### Nature

| Feature | Switch |
|---|---|
| Tree harvest areas also remove the stumps of felled trees: 3x the axe work of a tree, 1 log instead of 3 (`[TreeStumps]`). | `TreeStumps` |
| Plant trees: a free tree sapling (build menu, structures) grows from a tiny tree to a full tree over 30 minutes of game time, then is felled like any tree. A tree farm (new task menu) is a box dragged out like a landscape task, where builders plant saplings, fell the full-grown trees and replant the spots (`[Saplings]`). | `Saplings` |

### Building

New blocks, new buildings and faster ways to build.

| Feature | Switch |
|---|---|
| New blocks: stone bricks of 2 x 2, 2 x 4, 1 x 3 and 1 x 4 that stand on one supported block (the long ones bridge gaps between walls), a brick on end (1 x 1, 2 high), and wooden slabs (half high) of 1 x 1, 2 x 1, 2 x 2, 2 x 4, 1 x 3, 1 x 5 and 2 x 3 that hold on to the side of stone. | `CustomBlocks` |
| Wood ladder: bricktrons climb straight up and down it, and step onto or off it from the side halfway up; slower than stairs (`[Ladders] ClimbSpeed`, `RouteCost`), only with free hands or a bag. | `Ladders` |
| Wood blocks can be built out sideways from other wood: up to 5 blocks may hang into the air from a supported wood block (`[WoodOverhang] MaxOverhang`). | `WoodOverhang` |
| A drag build stops when the mouse button is let go, also over the interface or outside the window. | `DragRelease` |
| In build mode X picks which cell of the held block goes where the mouse points (Shift+X the other way), through all of its cells; choosing another block starts again. | `BlockAnchor` |
| One build task per team: every blueprint goes into it, and other build tasks are merged into it. | `GlobalBuildJob` |
| Hovering a blueprint shows the resources it still needs, with icon and count; a block of a build task, and the menu of a selected build task, show what the whole task still needs (red when the stockpiles hold too little). | `BuildNeeds` |
| Eyedropper: middle-click a block or building to build a copy. | `Eyedropper` |
| Pending blueprints are always visible. | `BlueprintsVisible` |
| Blueprints marked for deconstruction stay dark orange, also while their build task is not selected. | `DeconstructColour` |
| Move buildings: hold **M** and click a building, then place it; workers demolish the old one and build the new one from its materials (`[Building] MoveStructureKey`). | `MoveStructure` |
| Copy and paste: **Ctrl+C** and drag an area to copy its blocks, buildings and blueprints; **Ctrl+V** pastes them as blueprints, right-click rotates (`[Building] CopyKey`, `PasteKey`). | `CopyPaste` |
| The ward range spheres (shown while a ward is hovered or placed) are drawn fainter, so the ground under them can be seen (`[WardRange] Strength`). | `WardRangeFade` |

### Storage

| Feature | Switch |
|---|---|
| Large stockpile: 3 x 3 blocks, holds three times as much as the 2 x 2 stockpile. | `LargeStockpile` |
| Warehouse: a 9 x 6 hall in the build menu that holds 4500 resources of any kind; workers walk inside, and its brick works turns stored stone into bricks (60 planks, 80 bricks, 20 iron, 10 rope). | `Warehouse` |
| Mixed stockpiles: one pallet holds several resources, one resource type per column (four columns); raw stone (converted to bricks), bricks, logs, planks, iron, brimstone, crystals and other valuables (`[MixedStockpiles] NeverMix`) are never mixed. | `MixedStockpiles` |
| Workers prefer the nearest stockpile. | `StockpileChoice` |
| Idle workers consolidate small stockpiles into the fullest one. | `StockpileConsolidation` |
| "Empty stockpile" on the context wheel of a stockpile, large stockpile or warehouse: it takes nothing new and idle workers carry its contents to other storages. It stays empty (overhead icon, "Emptying" in its tooltip) until switched off; saved, host decides in multiplayer. | `EmptyStockpile` |
| A demolished or deconstructed stockpile (also the large one), warehouse, workshop or weapon stand drops what it holds instead of destroying it. | `StockpileSpill` |
| Hovering a stockpile lists what it holds next to the mouse (icon, name, count); a warehouse also shows how full it is in percent. The game's own icon-and-count line above it is hidden. | `StockpileTooltip` |

### Workshops, market and research

| Feature | Switch |
|---|---|
| Workshops can loop their queue until a stock limit per product (e.g. 20 fabric and 5 rope), each recipe waiting while there is enough. Turning on a recipe's limit also puts the recipe in the queue. | `CraftLoopLimit` |
| **New:** "Copy setup" and "Paste setup" in a workshop's menu copy its queue, looping and stock limits to another workshop of the same kind (only the recipes it can make). | `WorkshopSetup` |
| A workshop's priority (e.g. the research station) can be set like a task's: locked, low, medium or high. | `WorkshopPriority` |
| Market: a market hall in the build menu (crafting group, 8 planks, 6 bricks, 2 fabric) where workers trade any resource for any other, details below. | `Market` |
| Smithy, armoury and research station in the build menu (crafting group) for weapon, armour and colony upgrades, details below. | `Upgrades` |

**Market** (`[Market]`): a 15% fee; prices rise with trading and recover over time (a trade then asks more for the same lot, never refused), so trading never makes resources. Coal and steel trade too (steel can only be given). In the C# menu you pick what to give and what to get, see the price right now, and queue how many trades to make; each queued trade is made once. Several workers fetch for the same trade, and workers walk inside.

**Upgrades:**
- Weapon upgrades (Sharpened Blades, Heavy Blades, Fletching, Steady Aim, Winch) and armour upgrades (Chainmail, Padded Gambeson, Reinforced Helmet, Shield Rims, Warded Plate) in three tiers: iron, steel and crystal.
- Colony upgrades at the research station, paid in dark crystals: Housing (up to 15 more bricktrons), Work Methods, Stacking (+33% storage per tier, up to twice the room: 4 layers instead of 2, taller crates), Crystal Attunement, Light Boots, Firefly Lore, Giant Bricktrons, Scouting (more time between waves) and Efficient Mining (more stone, ore and crystal from digging and mining).
- 5 minutes of work per tier, with a progress bar over the researching worker; upgrades count for the whole team and are kept in the save.
- Researched tiers show on the soldiers' gear, and soldiers wear the researched chainmail, gambeson, plate, great helm and heater shield.
- Starting a research reserves its whole cost from the stockpiles at once and locks that upgrade at the team's other stations until it is finished or cancelled. Workers walk inside.
- **New:** in sandbox every research is free (there are no enemies to drop dark crystals); it still takes its usual time at its station. Other gamemodes pay as before.

### Army and combat

| Feature | Switch |
|---|---|
| Call to arms: soldiers per class (limited by the sets you own), ranged and melee rally points, and a role for each worker (for the selected workers, or per worker in a list of all workers). | `CallToArms` |
| Archers always hit visible targets, and arrows never hurt allies. | `ArcherAccuracy` |
| A ranged fighter (archer, arbalist, slinger, alchemist, artificer) whose shots would hit the castle or rock in between walks to a nearby place with a clear shot (the wall's edge, up to 8 blocks away) and shoots from there (`[RangedReposition]`). | `RangedReposition` |
| Melee fighters low on health retreat to a healing ward or the home crystal and heal before fighting again (`[Retreat]`). | `Retreat` |
| Workers without a soldier's gear no longer fight enemies that come close on their own; they leave them to the soldiers (an order to attack still sends them). | `UnarmedWorkers` |
| Artificers heal team mates in attack range with green healing bolts (10% health per bolt, one at a time). | `ArtificerHealing` |
| Critical hits from combat level 3: double damage at 5% (level 3), 10% (5), 15% (8) or 20% (10), with a red "2x damage" over the bricktron and a red glowing sword or shot. | `CriticalHits` |
| Mixed weapon stands: one stand holds the weapons and armour of all soldier classes at once; equipping takes one whole kit of a single class, the one a call to arms asks for, otherwise the class with the most kits on the stand (`[MixedRacks] PerSlot`). | `MixedRacks` |
| Every soldier kit is one item (Knight kit, Archer kit, ...): kit recipes make one bundle, a soldier unpacks it when equipping and packs a whole kit again when handing it in. Saves with kit bundles need the mod. | `KitBundles` |
| Enemy waves are no longer shut out by trees (planted rows included): enemies that break their way may route through trees and fell them, and the trees drop their logs. | `EnemyTreeClearing` |
| Healtron: a new enemy, a green corruptron that heals its allies with a pulse (8% health every 4 s within 6 voxels) and a beam on the most wounded (5% per second within 10 voxels), but hits for only 40%. Raids bring one now and then (15% per corruptron, at most 2 per raid, invasion from wave 3) (`[Healtron]`). | `Healtron` |

### Workers and experience

| Feature | Switch |
|---|---|
| Separate work and combat XP, levels 1-10 with +5% per level, shown in the name tag with XP bars and a level-up effect. | `Experience` |
| Giant bricktrons: sacrifice one worker and pay 50 dark crystals (or, without `DarkCrystals`, 1.5x the energy of a new bricktron) to upgrade another. It is 1.25x as big, 2x as fast at everything, has 2x health and carries 2.5x as much, but still counts as one bricktron; one per 5 bricktrons; first researched at the research station (`[GiantBricktron]`). | `GiantBricktron` |
| A dead worker respawns for a flat 100 energy instead of the price of a new bricktron. | `RespawnCost` |
| Dying bricktrons drop their gear instead of losing it. | `DropGearOnDeath` |

### Pathfinding

| Feature | Switch |
|---|---|
| Shorter routes, nearby stairs found from walls, bigger search budgets, no pause after picking something up, stairs usable under walkways, route searches limited to a time budget per frame, long bricks and slabs update the route graph over their whole length, and allies walk past rope bridge posts. | `Pathfinding` |
| Bricktrons walk through each other (allies never block a path; enemies still do), and a bricktron in a doorway no longer makes the area behind it unreachable. | `PassThroughAllies` |

### UI

| Feature | Switch |
|---|---|
| Resource list (icon, name, count) in the top-right corner. A resource stays listed from the first time it is stocked (in red at 0, also after loading), plus storage used and free (with free as a percentage). A **Resources** button on the right-hand bar shows and hides it (`[ResourceList] Shown`). | `ResourceList` |
| The minimap shows the island (terrain colours, height, slopes, walls and buildings) under the units. | `MinimapTerrain` |
| Minimap zoom: the mouse wheel over the minimap zooms in and out (1x to 6x, `[Minimap] Zoom`); zoomed in, it follows the camera, and clicks still move the camera to the place clicked. | `MinimapZoom` |
| **Ctrl+F** searches the world: matching bricktrons (name, job), enemies, buildings, blueprints and resources get a yellow ring (several searches with commas). | `WorldSearch` |
| Invasion: a big warning before the next wave at 30 and 15 seconds, and a countdown from 5. | `WaveWarning` |
| Invasion: an arrow on the screen points where the next wave will come from, 30 seconds before it. | `WaveDirection` |
| Respawn status under a dead worker's firefly (time left, missing energy). | `RespawnStatus` |
| Energy hint above your home crystal when the firefly of a killed enemy or worker arrives ("+N energy"). | `EnergyHint` |
| Units see 10% further through the fog of war (`[FogOfWar] VisionMultiplier`). | `WiderVision` |
| Castle Story Plus logo on the splash screen, main menu and loading screen, with the version number on the main menu and loading screen. | `PlusLogo` |
| **New:** coming back to the game with alt+tab no longer opens the construction menu: the build-mode key (Tab) is ignored for half a second after the window regains focus. | `FocusInputGuard` |

### Menus, saving and loading

| Feature | Switch |
|---|---|
| Game menus drawn in C# instead of the game's slow-to-build Lua menus, see below. | `CsMenus` |
| **Continue** on the title screen loads the latest save. | `ContinueButton` |
| **Save and return to main menu** and **Save and quit game** (with a confirmation) in the quit dialog. | `SaveAndLeave` |
| Exit closes the game at once (main menu or in a game), with no long wait while everything is torn down. | `FastQuit` |
| The game's launcher remembers "Launch in fullscreen", resolution and monitor. | `LauncherOptions` |
| Fast startup: `[Startup] FastStartup = true` (or the launch option `-faststartup`) skips the logo screens at startup. | `FastStartup` |
| New Invasion worlds: choose how often enemy waves come (every 5, 10, 15, 20 or 30 minutes), saved with the world. | `WaveInterval` |
| Game speed keys in single player: **1** normal, **2** 2x, **3** 3x (`[GameSpeed]`). | `SpeedKeys` |
| Autosave every 2 minutes of play (`[Saving] AutosaveMinutes`, 0 = off), one autosave slot per map. | `AutoSave` |
| Saving does not freeze the game: the quicksave and save menu write over several frames, and the files are written in the background and swapped in at once. | `FastSave` |
| Faster map loading that keeps the window responsive. | `FasterLoading` |
| Checks GitHub for a new release at startup and offers to update from the main menu (see [Updates](#updates)). | `UpdateCheck` |
| **Castle Story Plus settings** in the in-game Settings menu: autosave interval, firefly energy, giant bricktron values, debug mode, keys and feature switches. | (always on) |

**C# menus:** the whole in-game menu (`[LuaUi] CsGameMenu`, on by default; the game's GameMenu.lua is then not loaded at all), the gamemodes' intro, victory and defeat dialogs (`CsModals`) and the title screen's buttons (`CsMainMenu`) are drawn in C#. The other `[LuaUi]` switches draw parts of the Lua menu in C# when `CsGameMenu` is off.

### Performance

| Feature | Switch |
|---|---|
| More frames per second: shadows drawn to 150 blocks in 2 cascades instead of 400 in 4, and trees and terrain outside the screen cast a shadow only when it can land on the screen (`[RenderTuning]`). | `RenderTuning` |
| Less CPU time per frame: hidden icons over bricktrons and things no longer have their canvas rebuilt every frame, stockpile shelf displays only update when what they hold changes, a worker's resource search that found nothing is not repeated for 2 seconds, and bricktrons off the screen are not posed. | `FrameSavers` |
| Fixes memory leaks in the game itself (worker error store, highlights, pie menus, Lua menus, observers that outlive their game, stale reservations, logger names, copied adjective texts). | `GameLeakFixes` |
| Shorter garbage collection pauses: the Lua value heap (10 million slots, 280 MB, read by every collection) starts at 1 million slots and grows only when needed. | `LuaHeapShrink` |
| Shorter and fewer garbage collection pauses: each Lua script and coroutine gets stacks of 8,192 calls and 32,768 values (under 2 MB) instead of 131,072 of each (18 MB, made at once). | `LuaStackShrink` |

### Debug tools

The debug windows open only with **Debug mode** on (see [Configuration](#configuration)).

| Key | Feature | Switch |
|---|---|---|
| F9 | System log: what every bricktron does, problems, and the mod's log. | `SystemLog` |
| F8 | Debug menu (host only): energy, time of day, builders, healing, resources, enemies, invasion waves and the time between them, route view, 100% critrate. | `DebugMenu` |
| X | **New:** X-ray (also in the debug menu: Off, Semi for caves and tunnels, Full for ore only) shows bricktrons, buildings and ore under the ground; X toggles the semi x-ray, also without debug mode (`[Debug] XRayKey`). | `DebugMenu` |
| F7 | Performance window: frame rate, frame times (1% low, p95, p99), garbage collections, frame time and frame rate graphs, what takes the time each frame (every Update of the game and the mod, measured from the first time the window opens) and the last spikes. Frames over 100 ms (`[Performance] SpikeMs`) are logged with what took their time. | `Performance` |

## Installation

Download the package for your system from the [latest release](https://github.com/Tricky12321/CastleStoryPlus/releases/latest). Each package is complete: installer, BepInEx 5.4.23.5 and Castle Story Plus.

| System | Package | How |
|---|---|---|
| Windows | `CastleStoryPlus-<version>-windows.zip` | Unpack it and double-click `install.bat`. BepInEx loads by itself on Windows (`winhttp.dll`). |
| Linux | `CastleStoryPlus-<version>-linux.zip` | Unpack it and run `bash install.sh`. |

The installer finds Castle Story in your Steam libraries, installs BepInEx if it is missing, installs Castle Story Plus and points BepInEx at the bootstrap. Run it again at any time to update.

> **Linux launch option:** the game needs the Steam launch option `./run_bepinex.sh %command%`. If Steam is closed while the installer runs, it sets the option itself, in every Steam user's `localconfig.vdf`, and keeps a `.castlestoryplus.bak` backup. Otherwise set it once yourself: right-click Castle Story > Properties > Launch Options.

You can also run the installer straight from the repository without downloading a package first; it then downloads the latest package itself.

Linux:

```sh
curl -fsSL https://raw.githubusercontent.com/Tricky12321/CastleStoryPlus/main/installer/install.sh | bash
```

Windows (PowerShell):

```powershell
& ([scriptblock]::Create((New-Object Net.WebClient).DownloadString('https://raw.githubusercontent.com/Tricky12321/CastleStoryPlus/main/installer/install.ps1')))
```

Installer options (`install.sh` / `install.bat`):

| Linux | Windows | Effect |
|---|---|---|
| `--tag v0.5.0` | `-Tag v0.5.0` | install a specific release |
| `--game-dir DIR` | `-GameDir DIR` | game folder, if it is not found automatically |
| `--force` | `-Force` | reinstall even if up to date |
| `--uninstall` | `-Uninstall` | remove Castle Story Plus (BepInEx and its config stay) |

### Updates

Every release is a version tag (`v0.1.0`). At startup the plugin asks GitHub for the releases and picks the newest published release (not a draft or pre-release) that has a package for your system. If that release is newer than the installed plugin, the main menu shows a notice with **Update and restart**.

Update and restart first downloads the installer from that tag's source (`installer/` in the repository), falling back to the one shipped in `BepInEx/plugins/CastleStoryPlus/installer/`. The installer waits for the game to close, installs exactly that tag and starts the game again through Steam. Its output goes to `BepInEx/CastleStoryPlus.Update.log`. Turn the check off with `[Features] UpdateCheck = false`.

### Manual installation

1. Unpack **BepInEx 5.4.23.5** (`BepInEx_linux_x64_5.4.23.5.zip` or `BepInEx_win_x64_5.4.23.5.zip` from the [BepInEx releases](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5)) into the Castle Story folder (the one that contains `Castle Story_Data`).
2. Copy the release's `files/BepInEx` folder into the Castle Story folder.
3. Point BepInEx at the bootstrap:
   - **Linux:** in `run_bepinex.sh`, set `target_assembly="BepInEx/core/CastleStoryPlus.Bootstrap.dll"` and use the launch option above.
   - **Windows:** in `doorstop_config.ini`, set `target_assembly = BepInEx\core\CastleStoryPlus.Bootstrap.dll`.
4. Start the game. `BepInEx/LogOutput.log` should contain `Castle Story Plus <version> loaded`.

### Why a bootstrap?

Castle Story runs Unity's very old **Mono 2.6**. On Linux, MonoMod (the library behind HarmonyX) cannot change memory protection on this runtime, because its `mprotect` platform needs `Environment.SystemPageSize`, which Mono 2.6 does not have. On top of that, BepInEx's terminal fix resets the platform to `null`. As a result, every Harmony patch fails with a `NullReferenceException` in `NativeDetour`.

`CastleStoryPlus.Bootstrap.dll` is a small doorstop entry point that fixes this before BepInEx starts:
- it installs a MonoMod native platform that calls `mprotect` directly;
- it keeps that platform when BepInEx tries to reset it;
- it then starts the normal BepInEx preloader.

On Windows the bootstrap only starts BepInEx and changes nothing else.

The full story (the failing call chain, how the platform and the setter hook work, doorstop 3 and 4, and the assembly loading trap on Mono 2.6) is in [docs/BepInEx-on-Mono-2.6.md](docs/BepInEx-on-Mono-2.6.md).

## Configuration

The first start creates `BepInEx/config/com.tricky12321.castlestoryplus.cfg`. Edit it while the game is closed, or use the in-game settings window: **Settings > Castle Story Plus settings**. Changes there are written to the config file at once; `[Features]` switches apply after a restart.

| Setting | Default | Meaning |
|---|---|---|
| `[Features]` | | one `true`/`false` switch per feature (see [Features](#features)) |
| `[Economy] EnergyMultiplier` | `1.3` | energy per brewed firefly, relative to the blue crystal it costs |
| `[Saving] AutosaveMinutes` | `2` | minutes of play between autosaves, `0` = off |
| `[Building] MoveStructureKey`, `CopyKey`, `PasteKey` | M, Ctrl+C, Ctrl+V | keys for moving buildings and copy/paste |
| `[Debug] DebugMode` | off | opens the debug windows (F7, F8, F9) |
| `[Debug] XRayKey` | X | turns the semi x-ray on and off, also without debug mode |

**Debug mode** can be switched in the settings window or with the **Enable debug mode** checkbox in the game's Options > Gameplay (title screen and in game); it applies at once. The X-ray key does not work while placing blocks (X places a block by its next cell there), in a menu or while typing.

Call to arms settings (soldiers per class) are stored in the game's own player preferences. Rally points are set in-game and last for the current map.

## Reporting bugs

This is a fan mod in early development, so bugs will happen. Please report them as [GitHub issues](https://github.com/Tricky12321/CastleStoryPlus/issues) and include:
- your system (Windows or Linux) and the mod version;
- what you did and what happened;
- the log file `BepInEx/LogOutput.log` from the Castle Story folder.

Please do not report problems caused by the mod to Sauropod Studio. Turn the mod off first to check whether a problem also happens in the normal game.

## Roadmap

Castle Story Plus will keep growing beyond quality-of-life fixes. Planned next:

- **New maps:** new islands and scenarios to build on and defend.
- **More enemies:** new enemy types and invasion waves that call for different defences.
- **New creatures that work for you:** new units that take on tasks such as hauling, gathering or building, next to the bricktrons.

These plans can change. Ideas and wishes are welcome as [issues](https://github.com/Tricky12321/CastleStoryPlus/issues).

## Building from source

Requirements:
- the [.NET SDK](https://dotnet.microsoft.com/download) (8 or newer; the plugin targets .NET Framework 3.5 like the game);
- an installed copy of Castle Story with BepInEx;
- a copy of the **original** game assemblies in `Castle Story_Data/Managed.orig/`. The plugin compiles against these, and the [BepInEx assembly publicizer](https://github.com/BepInEx/BepInEx.AssemblyPublicizer) makes their private members reachable.

```sh
dotnet build -c Release
```

The build copies `CastleStoryPlus.dll` into the game's `BepInEx/plugins/CastleStoryPlus/`. If the game is not in the default Steam location:

```sh
dotnet build -c Release -p:GameDir="/path/to/Castle Story/"
```

How to add a building with its own model and textures is described in [docs/CustomBuildings.md](docs/CustomBuildings.md).

### Testing and the developer API

Three projects next to the plugin, never part of a release (see [docs/DevTools.md](docs/DevTools.md)):
- `CastleStoryPlus.Tests`: unit tests of the mod's pure logic, no game needed: `dotnet test CastleStoryPlus.Tests`.
- `CastleStoryPlus.DevTools`: a plugin of its own (`BepInEx/plugins/CastleStoryPlus.DevTools/`) with an HTTP API on `http://127.0.0.1:27860/api` into the running game, and in-game tests (pathfinding, ...) that run in the game that is loaded.
- `CastleStoryPlus.Mcp`: an MCP server that offers the API as tools to MCP clients (game state, log, screenshots, route searches, running the in-game tests, fast-forwarding the game up to 20x with `game_speed`). Registered in `.mcp.json`.

### Releasing

1. Bump `Version` in `CastleStoryPlus/Plugin.cs`, update `CHANGELOG.md` and commit.
2. Run `tools/release.sh`. It builds the plugin once and packs one package per system with `tools/package.sh`:
   - `dist/CastleStoryPlus-v<version>-linux.zip`: `install.sh` and BepInEx linux_x64;
   - `dist/CastleStoryPlus-v<version>-windows.zip`: `install.bat`, `install.ps1` and BepInEx win_x64.

   It then tags `v<version>` and creates the GitHub release with the [GitHub CLI](https://cli.github.com/). The release gets just the two packages and the install instructions from `tools/release-notes.md`.

Releases are built locally, because the build needs the game's own assemblies, which cannot go into the repository or CI.

## Project layout

```
CastleStoryPlus.Bootstrap/   doorstop entry point that makes HarmonyX work on Mono 2.6 (see above)
CastleStoryPlus/
  Plugin.cs            entry point: config, feature switches and Harmony setup
  Core/                feature attribute, Lua injection, IL helpers
  Workers/             per-worker data (XP, call to arms role): stored, saved and synced by the mod
  <Area>/              one folder per feature area (Army, Building, Combat, Economy, Experience,
                       Giant, Loading, Market, Menus, Metallurgy, Nature, Pathfinding, Performance,
                       Respawn, Saving, UI, Updates, Upgrades, WorkerAI, ...)
  Diagnostics/         modding aids and debug windows (debug menu, X-ray, SceneDump, WorkerTrace)
  Lua/                 Lua UI files added by the mod (the mod's own code only)
  Assets/              artwork (logo.png, plus.png), rendered by tools/make_logo.py
CastleStoryPlus.UI/          the C# in-game menu's builders and views (CastleStoryPlus.UI.dll, reloadable in game)
CastleStoryPlus.DevTools/    developer plugin (not released): HTTP API into the game, in-game test runner and tests
CastleStoryPlus.Mcp/         MCP server (stdio), calls the DevTools API
CastleStoryPlus.Tests/       unit tests of the mod's pure logic (NUnit, dotnet test)
installer/             install.sh (Linux), install.ps1 + install.bat (Windows): install and update from GitHub releases
tools/                 package.sh (Linux + Windows packages), release.sh (tag + GitHub release), release-notes.md, make_logo.py
docs/                  technical notes and the gallery
CHANGELOG.md           all changes, in English
Directory.Build.props  shared build settings (target framework, game path)
```

## How it works

- **Patches:** each change is a small Harmony patch class tagged with the feature it belongs to. A feature switched off in the config is simply not patched.
- **Lua UI:** the game's own Lua files are not shipped. When the game loads one, the mod inserts its code at fixed anchors. It adds its own Lua files from `BepInEx/plugins/CastleStoryPlus/Lua/`, and gives Lua a `CastleStoryPlus` table of C# functions.
- **Per-worker data** (XP, call to arms role):
  - It is saved as extra properties on the game's existing `CharacterState` component, so saves stay loadable by the unmodded game. Saves made with the earlier prototype keep their XP.
  - It is synced by appending it to that component's network data.

## Multiplayer

The mod adds network data and commands, so every player in a game must run the same version of the mod.

## Disclaimer

This is an unofficial fan mod and is not affiliated with Sauropod Studio. Use it at your own risk and back up your saves.
