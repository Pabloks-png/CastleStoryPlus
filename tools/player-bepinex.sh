#!/usr/bin/env bash
# Puts BepInEx into a Unity 6 player build (CastleStoryPlus.Player/Build/<platform>), so the mod runs in the port:
# BepInEx's core, its doorstop, and the start script (Linux) with doorstop pointed at BepInEx's own preloader. The
# player runs a current Mono, so the Mono 2.6 workarounds of CastleStoryPlus.Bootstrap are not needed there.
#
#   tools/player-bepinex.sh <player build dir> <BepInEx install dir>
#
# The BepInEx install is a folder holding BepInEx/core and doorstop (libdoorstop.so and run_bepinex.sh for Linux,
# winhttp.dll and doorstop_config.ini for Windows), such as the game's own install or an unpacked BepInEx 5 release.
# The plugins themselves come from the mod's build: dotnet build -p:UnityTarget=player.
set -euo pipefail
if [ $# -ne 2 ]; then
	sed -n '2,10p' "$0" | sed 's/^# \{0,1\}//'
	exit 1
fi
PLAYER="${1%/}"
SOURCE="${2%/}"
if [ ! -d "$SOURCE/BepInEx/core" ]; then
	echo "no BepInEx/core in $SOURCE" >&2
	exit 1
fi

mkdir -p "$PLAYER/BepInEx/core" "$PLAYER/BepInEx/plugins" "$PLAYER/BepInEx/patchers" "$PLAYER/BepInEx/config"
# BepInEx's own files only, not ours (the bootstrap and anything else of the mod's).
find "$SOURCE/BepInEx/core" -maxdepth 1 -type f ! -name 'CastleStoryPlus*' -exec cp -u {} "$PLAYER/BepInEx/core/" \;

# BepInEx starts the plugins from CastleStoryPlus.Player.ModLoader (see there), not from Unity's Application.
CONFIG="$PLAYER/BepInEx/config/BepInEx.cfg"
if ! grep -q '^Type = ModLoader' "$CONFIG" 2>/dev/null; then
	if grep -q '^\[Preloader.Entrypoint\]' "$CONFIG" 2>/dev/null; then
		sed -i -e '/^\[Preloader.Entrypoint\]/,/^\[/{s/^Assembly = .*/Assembly = CastleStoryPlus.Player.dll/;s/^Type = .*/Type = ModLoader/;s/^Method = .*/Method = .cctor/}' "$CONFIG"
	else
		printf '[Preloader.Entrypoint]\n\nAssembly = CastleStoryPlus.Player.dll\n\nType = ModLoader\n\nMethod = .cctor\n' >> "$CONFIG"
	fi
fi

if [ -f "$PLAYER/CastleStoryPlus.x86_64" ]; then
	cp -u "$SOURCE/libdoorstop.so" "$PLAYER/"
	[ -f "$SOURCE/.doorstop_version" ] && cp -u "$SOURCE/.doorstop_version" "$PLAYER/"
	sed -e 's|^target_assembly=.*|target_assembly="BepInEx/core/BepInEx.Preloader.dll"|' \
		-e 's|^executable_name=.*|executable_name="CastleStoryPlus.x86_64"|' \
		"$SOURCE/run_bepinex.sh" > "$PLAYER/run_bepinex.sh"
	chmod +x "$PLAYER/run_bepinex.sh"
fi
if [ -f "$PLAYER/CastleStoryPlus.exe" ]; then
	cp -u "$SOURCE/winhttp.dll" "$PLAYER/"
	sed -e 's|^target_assembly *=.*|target_assembly = BepInEx\\core\\BepInEx.Preloader.dll|' \
		"$SOURCE/doorstop_config.ini" > "$PLAYER/doorstop_config.ini"
	[ -f "$SOURCE/.doorstop_version" ] && cp -u "$SOURCE/.doorstop_version" "$PLAYER/"
fi
echo "BepInEx $(ls "$PLAYER/BepInEx/core" | wc -l) core files -> $PLAYER"
