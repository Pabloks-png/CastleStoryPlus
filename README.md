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


## Highlights

- **Smarter workers:** they find work on their own, reserve tasks and resources, skip unreachable jobs and dig themselves out of holes.
- **Task control:** pause tasks, limit crew size, give a blueprint top priority, or send selected bricktrons straight to a task.
- **Mining and metallurgy:** coal, steel, mine shafts, ore mines and per-resource quarry limits.
- **New buildings and blocks:** warehouse, large and mixed stockpiles, market hall, smithy, armoury, research station, long bricks, wooden slabs and ladders.
- **Building tools:** copy and paste, move buildings, eyedropper and a single build task per team.
- **Research and upgrades:** weapon, armour and colony upgrades in iron, steel and crystal tiers.
- **Army:** call to arms with rally points, smarter archers, retreating and healing fighters, critical hits and a new healing enemy.
- **Experience:** separate work and combat XP with levels, and giant bricktrons.
- **UI:** resource list, terrain minimap with zoom, world search, wave warnings and C# game menus.
- **Saving and performance:** autosave, non-freezing saves, faster loading, more frames per second and fixes for the game's memory leaks.
- **Every feature can be switched off** in the config file or the in-game settings.

## Install

Download the package for your system from the [latest release](https://github.com/Tricky12321/CastleStoryPlus/releases/latest). Each package is complete: installer, BepInEx 5.4.23.5 and Castle Story Plus.

| System | Package | How |
|---|---|---|
| Windows | `CastleStoryPlus-<version>-windows.zip` | Unpack it and double-click `install.bat`. |
| Linux | `CastleStoryPlus-<version>-linux.zip` | Unpack it and run `bash install.sh`. |

The installer finds Castle Story in your Steam libraries, installs BepInEx if it is missing and installs Castle Story Plus. Run it again at any time to update; the game also offers updates from the main menu. In multiplayer every player needs the same version of the mod.

> **Linux launch option:** the game needs the Steam launch option `./run_bepinex.sh %command%`. If Steam is closed while the installer runs, it sets the option itself. Otherwise set it once yourself: right-click Castle Story > Properties > Launch Options.

One-line installers, installer options, updates and manual installation: see [Installation](docs/Installation.md).

## Documentation

| Page | Contents |
|---|---|
| [Features](docs/Features.md) | every feature per area, with its `[Features]` switch |
| [Installation](docs/Installation.md) | installers and options, updates, manual installation, why a bootstrap, multiplayer |
| [Configuration](docs/Configuration.md) | the config file, in-game settings and debug mode |
| [Gallery](docs/Gallery.md) | pictures of every building, block, armour stand and stockpile |
| [Contributing](docs/Contributing.md) | reporting bugs and the roadmap |
| [Roadmap](docs/Roadmap.md) | features planned over time: animals (taming, stables, training) and portals |
| [Development](docs/Development.md) | building from source, testing, releasing, project layout, how it works |
| [DevTools](docs/DevTools.md) | the developer plugin, HTTP API, in-game tests and MCP server |
| [Performance](docs/Performance.md) | measuring frame time and memory, what was found and changed |
| [Lua memory leaks](docs/LuaMemoryLeaks.md) | how the leaks of the game's Lua interface were found and fixed |
| [Custom buildings](docs/CustomBuildings.md) | adding a building with its own model and textures |
| [Generated structures](docs/GeneratedStructures.md) | rules for structures the mod builds from blocks: 1x2 bricks in a bond, crust, building order |
| [Guardians](docs/Guardians.md) | rules for the treasure guardians: how they look, model, icon, gait and attack |
| [BepInEx on Mono 2.6](docs/BepInEx-on-Mono-2.6.md) | why and how the bootstrap makes HarmonyX work on the game's runtime |
| [Changelog](CHANGELOG.md) | all changes |

---

Unofficial fan mod, not affiliated with Sauropod Studio. Use it at your own risk and back up your saves.
