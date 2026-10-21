#!/bin/sh
# Rebuild tools/data/valheim_prefabs.txt from the installed Valheim client (run after Valheim updates).
VALHEIM="${VALHEIM_DIR:-$HOME/.steam/steam/steamapps/common/Valheim}"
OUT="$(dirname "$0")/data/valheim_prefabs.txt"
{
  echo "# Valheim prefab names (one per line), extracted from valheim_Data/StreamingAssets/SoftRef/manifest_extended"
  echo "# by tools/extract_prefabs.sh. Used by the tests to verify every prefab named in assets/mappings."
  strings -n 4 "$VALHEIM/valheim_Data/StreamingAssets/SoftRef/manifest_extended" \
    | grep -oE "[A-Za-z0-9_/ ().-]+\.prefab" | sed 's#.*/##; s/\.prefab$//' | sort -u
} > "$OUT"
echo "$(grep -vc '^#' "$OUT") prefabs -> $OUT"
