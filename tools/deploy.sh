#!/bin/sh
# Copy the built plugin into a Valheim install's BepInEx/plugins/Bretaheim.
# Usage: tools/deploy.sh [valheim dir]   (default: the Steam client install)
#
# Files are written beside their destination and renamed over it: a running game keeps reading
# its old copy (Mono maps the DLL; overwriting it in place corrupts the loaded assembly), and the
# new one is used at the next start. Restart Valheim to pick up a deploy.
set -e
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VALHEIM="${1:-$HOME/.steam/steam/steamapps/common/Valheim}"
DEST="$VALHEIM/BepInEx/plugins/Bretaheim"
[ -d "$VALHEIM/BepInEx/plugins" ] || { echo "no BepInEx/plugins in $VALHEIM"; exit 1; }
dotnet build "$ROOT/src/Bretaheim/Bretaheim.csproj" -c Release -v q >/dev/null
mkdir -p "$DEST/mappings"
put() { cp "$1" "$2.tmp$$" && mv -f "$2.tmp$$" "$2"; }
put "$ROOT/src/Bretaheim/bin/Release/netstandard2.1/Bretaheim.dll" "$DEST/Bretaheim.dll"
for f in "$ROOT"/assets/mappings/*.json; do put "$f" "$DEST/mappings/$(basename "$f")"; done
echo "deployed to $DEST"
if pgrep -f "$(basename "$VALHEIM")/valheim" >/dev/null 2>&1 || pgrep -x valheim.x86_64 >/dev/null 2>&1; then
  echo "NOTE: Valheim is running; it keeps the old plugin until you quit the game completely and restart."
fi
