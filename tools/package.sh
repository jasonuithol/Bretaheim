#!/bin/sh
# Build the plugin and produce release/tarbaby-Bretaheim-<version>.zip (Thunderstore layout).
# The version is read from ThunderstoreAssets/manifest.json and must match Plugin.cs and CHANGELOG.md.
set -e
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VERSION=$(python3 -c "import json;print(json.load(open('$ROOT/ThunderstoreAssets/manifest.json'))['version_number'])")
grep -q "PluginVersion = \"$VERSION\"" "$ROOT/src/Bretaheim/Plugin.cs" || { echo "Plugin.cs version != $VERSION"; exit 1; }
grep -q "^## $VERSION" "$ROOT/ThunderstoreAssets/CHANGELOG.md" || { echo "CHANGELOG.md has no $VERSION entry"; exit 1; }

dotnet build "$ROOT/src/Bretaheim/Bretaheim.csproj" -c Release -v q "$@"
OUT="$ROOT/src/Bretaheim/bin/Release/netstandard2.1"
STAGE="$ROOT/staging"
rm -rf "$STAGE" && mkdir -p "$STAGE/plugins/Bretaheim/mappings" "$ROOT/release"
cp "$OUT/Bretaheim.dll" "$STAGE/plugins/Bretaheim/"
cp "$ROOT"/assets/mappings/*.json "$STAGE/plugins/Bretaheim/mappings/"
cp "$ROOT"/ThunderstoreAssets/manifest.json "$ROOT"/ThunderstoreAssets/README.md \
   "$ROOT"/ThunderstoreAssets/CHANGELOG.md "$ROOT"/ThunderstoreAssets/icon.png "$STAGE/"
ZIP="$ROOT/release/tarbaby-Bretaheim-$VERSION.zip"
rm -f "$ZIP" && (cd "$STAGE" && zip -qr "$ZIP" .)
echo "$ZIP"
