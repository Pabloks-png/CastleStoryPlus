# Castle Story Plus on Unity 6 (preview)

Castle Story on a current Unity (6000.6), with the Castle Story Plus mod. Nothing of the game is in this package: the
port is made from your own Castle Story install the first time it starts, so you must own and install Castle Story
(Steam). Your install is only read, never changed, and the port shares its saves and settings (`Info/`).

This is a preview. Audio is untested, and it has only been tested on Linux and under Proton.

## Start

1. Unpack this folder anywhere.
2. Start it:
   - Linux: `./run_bepinex.sh` (starts `CastleStoryPlus.x86_64` with the mod; the player alone has no mod).
   - Windows: `CastleStoryPlus.exe`.
3. The first start shows the installer. It finds Castle Story in your Steam libraries, or you type its folder. Install
   checks every game file, converts the game for Unity 6 (a minute or so) and starts the game.

The player needs the game for its own system: the Linux version on Linux, the Windows version on Windows. If the
check fails, verify the game's files in Steam and start again. `install.log` next to the player has the details.

After an update of this package, unpack it over the old folder; the next start installs again.
