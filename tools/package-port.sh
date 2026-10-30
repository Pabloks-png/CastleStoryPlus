#!/usr/bin/env bash
# Builds the Unity 6 port's release, one zip per system:
#   dist/CastleStoryPlus-Port-<version>-linux.zip     start with run_bepinex.sh
#   dist/CastleStoryPlus-Port-<version>-windows.zip   start with CastleStoryPlus.exe
# Each holds a fresh player build (PlayerBuild.Release<os>, nothing of the game), its first-start installer
# (Installer/: CastleStoryPlus.Port, classdata.tpk, the game file manifest), BepInEx 5 and the mod
# (BepInEx/plugins/CastleStoryPlus).
#
#   tools/package-port.sh [linux|windows]...   (both when none is given)
#
# The mod is built against the game code an install patched into the development player
# (CastleStoryPlus.Player/Build/<Linux|Windows>), so that must have been installed into first. The Unity editor must be
# closed (UNITY: the editor, default ~/Unity/Hub/Editor/6000.6.7f1/Editor/Unity).
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="v$(sed -n 's/.*public const string Version = "\(.*\)";.*/\1/p' "$ROOT/CastleStoryPlus/Plugin.cs")"
BEPINEX_VERSION="5.4.23.5"
UNITY="${UNITY:-$HOME/Unity/Hub/Editor/6000.6.7f1/Editor/Unity}"
PROJECT="$ROOT/CastleStoryPlus.Player"
CACHE="$ROOT/dist/cache"
mkdir -p "$CACHE"

# pack <os> <Platform> <bepinex zip name>
pack() {
	local os="$1" platform="$2" bepinex="$3"
	local name="CastleStoryPlus-Port-$VERSION-$os"
	local out="$ROOT/dist/$name"
	local dev="$PROJECT/Build/$platform"
	local build="$PROJECT/Build/Release/$platform"
	if [ ! -f "$dev/CastleStoryPlus_Data/Managed/game-assemblies.txt" ]; then
		echo "error: no install in $dev; the mod is built against the game code an install patched there" >&2
		exit 1
	fi

	# The player, from an empty folder.
	"$UNITY" -batchmode -quit -projectPath "$PROJECT" -executeMethod "CastleStoryPlus.Player.Editor.PlayerBuild.Release$platform" \
		-logFile "$ROOT/dist/player-$os.log" >/dev/null 2>&1 || true
	if ! grep -q "\[PlayerBuild\] Succeeded" "$ROOT/dist/player-$os.log"; then
		echo "error: the $platform player did not build, see dist/player-$os.log" >&2
		exit 1
	fi
	"$ROOT/tools/player-installer.sh" "$build"

	# BepInEx, from its release.
	if [ ! -f "$CACHE/$bepinex" ]; then
		curl -fsSL -o "$CACHE/$bepinex" "https://github.com/BepInEx/BepInEx/releases/download/v$BEPINEX_VERSION/$bepinex"
	fi
	rm -rf "$CACHE/bepinex-$os"
	python3 -m zipfile -e "$CACHE/$bepinex" "$CACHE/bepinex-$os"
	"$ROOT/tools/player-bepinex.sh" "$build" "$CACHE/bepinex-$os"

	# The mod (not the developer tools).
	local project
	for project in CastleStoryPlus CastleStoryPlus.UI; do
		dotnet build "$ROOT/$project" -c Release -p:UnityTarget=player -p:PlayerPlatform="$platform" -nologo -v quiet
	done
	local bin="$ROOT/CastleStoryPlus/bin/player/Release/net48"
	local plugin="$build/BepInEx/plugins/CastleStoryPlus"
	mkdir -p "$plugin"
	cp "$bin/CastleStoryPlus.dll" "$ROOT/CastleStoryPlus.UI/bin/player/Release/net48/CastleStoryPlus.UI.dll" "$plugin/"
	cp -r "$bin/Lua" "$bin/Assets" "$plugin/"
	echo "$VERSION" > "$plugin/version.txt"

	rm -rf "$out" "$ROOT/dist/$name.zip"
	mkdir -p "$out"
	cp -r "$build/." "$out/"
	cp "$ROOT/installer/README-Port.md" "$out/README.md"
cp "$ROOT/LICENSE" "$ROOT/NOTICE" "$out/"
	# Unity's debugging symbols for the player are not shipped.
	rm -rf "$out"/*_BurstDebugInformation_DoNotShip "$out"/*_BackUpThisFolder_ButDontShipItWithYourGame

	# Nothing of the game, and nothing for development only.
	local bad
	bad="$(find "$out" \( -iname "*DevTools*" -o -iname "*Mcp*" -o -iname "*.Tests*" -o -iname "*.pdb" \
		-o -name "Content" -o -name "Info" -o -name "game-assemblies.txt" -o -name "install.log" \
		-o -name "Assembly-CSharp*.dll" -o -name "Brix.*.dll" -o -name "globalgamemanagers.player" \) -print)"
	if [ -n "$bad" ]; then
		echo "error: files that do not belong in the release:" >&2
		echo "$bad" >&2
		exit 1
	fi

	(cd "$ROOT/dist" && python3 - "$name" <<'PY'
import os, sys, zipfile
name = sys.argv[1]
with zipfile.ZipFile(name + ".zip", "w", zipfile.ZIP_DEFLATED) as z:
    for folder, _, files in os.walk(name):
        for f in sorted(files):
            path = os.path.join(folder, f)
            info = zipfile.ZipInfo.from_file(path, path)
            info.compress_type = zipfile.ZIP_DEFLATED
            with open(path, "rb") as data:
                z.writestr(info, data.read())
PY
	)
	echo "packed dist/$name.zip"
}

targets=("$@")
if [ ${#targets[@]} -eq 0 ]; then
	targets=(linux windows)
fi
for target in "${targets[@]}"; do
	case "$target" in
		linux) pack linux Linux "BepInEx_linux_x64_$BEPINEX_VERSION.zip" ;;
		windows) pack windows Windows "BepInEx_win_x64_$BEPINEX_VERSION.zip" ;;
		*) echo "unknown system: $target" >&2; exit 1 ;;
	esac
done
