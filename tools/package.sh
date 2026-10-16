#!/bin/sh
# Build the plugin and produce release/tarbaby-U5InValheim-<version>.zip (Thunderstore layout).
# The version is read from ThunderstoreAssets/manifest.json and must match Plugin.cs and CHANGELOG.md.
set -e
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VERSION=$(python3 -c "import json;print(json.load(open('$ROOT/ThunderstoreAssets/manifest.json'))['version_number'])")
grep -q "PluginVersion = \"$VERSION\"" "$ROOT/src/U5InValheim/Plugin.cs" || { echo "Plugin.cs version != $VERSION"; exit 1; }
grep -q "^## $VERSION" "$ROOT/ThunderstoreAssets/CHANGELOG.md" || { echo "CHANGELOG.md has no $VERSION entry"; exit 1; }

dotnet build "$ROOT/src/U5InValheim/U5InValheim.csproj" -c Release -v q "$@"
OUT="$ROOT/src/U5InValheim/bin/Release/netstandard2.1"
STAGE="$ROOT/staging"
rm -rf "$STAGE" && mkdir -p "$STAGE/plugins/U5InValheim/mappings" "$ROOT/release"
cp "$OUT/U5InValheim.dll" "$STAGE/plugins/U5InValheim/"
cp "$ROOT"/assets/mappings/*.json "$STAGE/plugins/U5InValheim/mappings/"
cp "$ROOT"/ThunderstoreAssets/manifest.json "$ROOT"/ThunderstoreAssets/README.md \
   "$ROOT"/ThunderstoreAssets/CHANGELOG.md "$ROOT"/ThunderstoreAssets/icon.png "$STAGE/"
ZIP="$ROOT/release/tarbaby-U5InValheim-$VERSION.zip"
rm -f "$ZIP" && (cd "$STAGE" && zip -qr "$ZIP" .)
echo "$ZIP"
