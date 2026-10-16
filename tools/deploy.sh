#!/bin/sh
# Copy the built plugin into a Valheim install's BepInEx/plugins/U5InValheim.
# Usage: tools/deploy.sh [valheim dir]   (default: the Steam client install)
set -e
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VALHEIM="${1:-$HOME/.steam/steam/steamapps/common/Valheim}"
DEST="$VALHEIM/BepInEx/plugins/U5InValheim"
[ -d "$VALHEIM/BepInEx/plugins" ] || { echo "no BepInEx/plugins in $VALHEIM"; exit 1; }
dotnet build "$ROOT/src/U5InValheim/U5InValheim.csproj" -c Release -v q
mkdir -p "$DEST/mappings"
cp "$ROOT/src/U5InValheim/bin/Release/netstandard2.1/U5InValheim.dll" "$DEST/"
cp "$ROOT"/assets/mappings/*.json "$DEST/mappings/"
echo "deployed to $DEST"
