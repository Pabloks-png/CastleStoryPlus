#!/usr/bin/env bash
# Puts the installer into a Unity 6 player build (CastleStoryPlus.Player/Build/<platform>): CastleStoryPlus.Port with
# its libraries, the class database and the game files manifest for the build's platform, in Installer/. The player
# runs it at first start (InstallerScreen), when it has no converted content yet.
#
#   tools/player-installer.sh <player build dir>
#
# classdata.tpk and the manifests come from CastleStoryPlus.Port/Data (not in git, see docs/Unity6Port.md).
set -euo pipefail
if [ $# -ne 1 ]; then
	sed -n '2,8p' "$0" | sed 's/^# \{0,1\}//'
	exit 1
fi
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PLAYER="${1%/}"
DATA="$ROOT/CastleStoryPlus.Port/Data"
if [ -f "$PLAYER/CastleStoryPlus.exe" ]; then
	MANIFEST=game-files-windows.sha256
elif [ -f "$PLAYER/CastleStoryPlus.x86_64" ]; then
	MANIFEST=game-files.sha256
else
	echo "no player in $PLAYER" >&2
	exit 1
fi
for file in classdata.tpk "$MANIFEST"; do
	if [ ! -f "$DATA/$file" ]; then
		echo "missing $DATA/$file" >&2
		exit 1
	fi
done

dotnet publish "$ROOT/CastleStoryPlus.Port" -c Release -o "$PLAYER/Installer" --nologo -v quiet
rm -f "$PLAYER/Installer/CastleStoryPlus.Port.deps.json" "$PLAYER/Installer/CastleStoryPlus.Port.pdb"
cp "$DATA/classdata.tpk" "$PLAYER/Installer/"
cp "$DATA/$MANIFEST" "$PLAYER/Installer/game-files.sha256"
echo "installer ($(ls "$PLAYER/Installer" | wc -l) files, manifest $MANIFEST) -> $PLAYER/Installer"
