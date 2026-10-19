#!/usr/bin/env bash
# Starts Castle Story with BepInEx and the mod straight away, without the launcher's Play button: the game binary
# through run_bepinex.sh, with the arguments the launcher gives it and the Steam app id (Steam must be running).
#
#   tools/start-game.sh            start the game (refused while it runs)
#   tools/start-game.sh --restart  close the running game first (its own Quit via the API, else SIGKILL after 15 s)
#   tools/start-game.sh --wait     also wait until the DevTools API answers (http://127.0.0.1:27860)
#
# The game's output goes to BepInEx/start-game.log. Window size: GAME_WIDTH, GAME_HEIGHT, GAME_FULLSCREEN (default
# 2560 x 1440, fullscreen 1).
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GAME_DIR="$(sed -n 's:.*<GameDir[^>]*>\(.*\)</GameDir>.*:\1:p' "$ROOT/Directory.Build.props" | head -1)"
GAME_DIR="${GAME_DIR%/}"
GAME_DIR="${GAME_DIR/#\$(HOME)/$HOME}"
if [ ! -x "$GAME_DIR/Castle Story" ]; then
	GAME_DIR="$HOME/.local/share/Steam/steamapps/common/Castle Story"
fi
EXE="$GAME_DIR/Castle Story"
PORT=27860
restart=0
wait=0
for arg in "$@"; do
	case "$arg" in
		--restart) restart=1 ;;
		--wait) wait=1 ;;
		*) echo "Unknown option $arg" >&2; exit 2 ;;
	esac
done

running() {
	pgrep -x "Castle Story" >/dev/null 2>&1
}

if running; then
	if [ $restart -eq 0 ]; then
		echo "Castle Story is running already (--restart closes it first)" >&2
		exit 1
	fi
	# The game's own Quit through the DevTools API first: a SIGTERM often left the Linux player hanging with a frozen
	# window instead of closing. If it is not gone in 15 seconds (no API, or it hangs on the way out), it is killed
	# outright (SIGKILL), never sent SIGTERM.
	curl -s -m 3 -X POST "http://127.0.0.1:$PORT/api/quit" >/dev/null 2>&1 || true
	for _ in $(seq 1 15); do
		running || break
		sleep 1
	done
	if running; then
		echo "Castle Story did not close; killing it" >&2
		pkill -9 -x "Castle Story" || true
		for _ in $(seq 1 10); do
			running || break
			sleep 1
		done
	fi
	# The launcher may still be open: it would start a second game on Play.
	pkill -x castlestory-launcher 2>/dev/null || true
fi
if ! pgrep -x steam >/dev/null 2>&1 && ! pgrep -f ubuntu12_32/steam >/dev/null 2>&1; then
	echo "Steam is not running: the game needs it" >&2
	exit 1
fi

cd "$GAME_DIR"
export SteamAppId=227860
export SteamGameId=227860
setsid ./run_bepinex.sh "./Castle Story" \
	-screen-width "${GAME_WIDTH:-2560}" -screen-height "${GAME_HEIGHT:-1440}" -screen-fullscreen "${GAME_FULLSCREEN:-1}" -adapter 0 \
	>"$GAME_DIR/BepInEx/start-game.log" 2>&1 < /dev/null &
echo "Started Castle Story (log: $GAME_DIR/BepInEx/start-game.log)"

if [ $wait -eq 1 ]; then
	for _ in $(seq 1 180); do
		if curl -s -m 2 "http://127.0.0.1:$PORT/api/status" >/dev/null 2>&1; then
			echo "DevTools API is up on port $PORT"
			exit 0
		fi
		if ! running && [ "$_" -gt 20 ]; then
			echo "The game exited; see $GAME_DIR/BepInEx/start-game.log" >&2
			exit 1
		fi
		sleep 1
	done
	echo "The DevTools API did not answer within 3 minutes" >&2
	exit 1
fi
