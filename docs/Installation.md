# Installation

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
| `--tag v0.5.1` | `-Tag v0.5.1` | install a specific release |
| `--game-dir DIR` | `-GameDir DIR` | game folder, if it is not found automatically |
| `--force` | `-Force` | reinstall even if up to date |
| `--uninstall` | `-Uninstall` | remove Castle Story Plus (BepInEx and its config stay) |

## Updates

Every release is a version tag (`v0.1.0`). At startup the plugin asks GitHub for the releases and picks the newest published release (not a draft or pre-release) that has a package for your system. If that release is newer than the installed plugin, the main menu shows a notice with **Update and restart**.

Update and restart first downloads the installer from that tag's source (`installer/` in the repository), falling back to the one shipped in `BepInEx/plugins/CastleStoryPlus/installer/`. The installer waits for the game to close, installs exactly that tag and starts the game again through Steam. Its output goes to `BepInEx/CastleStoryPlus.Update.log`. Turn the check off with `[Features] UpdateCheck = false`.

## Manual installation

1. Unpack **BepInEx 5.4.23.5** (`BepInEx_linux_x64_5.4.23.5.zip` or `BepInEx_win_x64_5.4.23.5.zip` from the [BepInEx releases](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5)) into the Castle Story folder (the one that contains `Castle Story_Data`).
2. Copy the release's `files/BepInEx` folder into the Castle Story folder.
3. Point BepInEx at the bootstrap:
   - **Linux:** in `run_bepinex.sh`, set `target_assembly="BepInEx/core/CastleStoryPlus.Bootstrap.dll"` and use the Linux launch option above.
   - **Windows:** in `doorstop_config.ini`, set `target_assembly = BepInEx\core\CastleStoryPlus.Bootstrap.dll`.
4. Start the game. `BepInEx/LogOutput.log` should contain `Castle Story Plus <version> loaded`.

## Why a bootstrap?

Castle Story runs Unity's very old **Mono 2.6**. On Linux, MonoMod (the library behind HarmonyX) cannot change memory protection on this runtime, because its `mprotect` platform needs `Environment.SystemPageSize`, which Mono 2.6 does not have. On top of that, BepInEx's terminal fix resets the platform to `null`. As a result, every Harmony patch fails with a `NullReferenceException` in `NativeDetour`.

`CastleStoryPlus.Bootstrap.dll` is a small doorstop entry point that fixes this before BepInEx starts:
- it installs a MonoMod native platform that calls `mprotect` directly;
- it keeps that platform when BepInEx tries to reset it;
- it then starts the normal BepInEx preloader.

On Windows the bootstrap only starts BepInEx and changes nothing else.

The full story (the failing call chain, how the platform and the setter hook work, doorstop 3 and 4, and the assembly loading trap on Mono 2.6) is in [BepInEx on Mono 2.6](BepInEx-on-Mono-2.6.md).

## Multiplayer

The mod adds network data and commands, so every player in a game must run the same version of the mod.
