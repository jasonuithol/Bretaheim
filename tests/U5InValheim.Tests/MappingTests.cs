using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace U5InValheim.Tests
{
    /// <summary>The assets/mappings tables only name prefabs that exist in Valheim and cover every U5 id.</summary>
    public class MappingTests
    {
        static readonly HashSet<string> Prefabs = new HashSet<string>(
            File.ReadAllLines(Path.Combine(Env.RepoRoot, "tools/data/valheim_prefabs.txt")).Where(l => !l.StartsWith("#")));

        static JsonElement Load(string name)
            => JsonDocument.Parse(File.ReadAllText(Path.Combine(Env.RepoRoot, "assets/mappings", name))).RootElement;

        /// <summary>Every string property called prefab / base / extra / outfit / alt.prefab in the document.</summary>
        static IEnumerable<string> PrefabNames(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object)
                foreach (var p in e.EnumerateObject())
                {
                    bool named = p.Name == "prefab" || p.Name == "extra" || p.Name == "outfit" || p.Name == "prefab_by_circle"
                                 || (p.Name == "base" && p.Value.ValueKind == JsonValueKind.String);
                    if (named && p.Value.ValueKind == JsonValueKind.String) yield return p.Value.GetString();
                    else if (named && p.Value.ValueKind == JsonValueKind.Array)
                        foreach (var x in p.Value.EnumerateArray()) yield return x.GetString();
                    else foreach (var n in PrefabNames(p.Value)) yield return n;
                }
            else if (e.ValueKind == JsonValueKind.Array)
                foreach (var x in e.EnumerateArray())
                    foreach (var n in PrefabNames(x)) yield return n;
        }

        // "human"/"player" are our own humanoid bases; "hildir:..." are outfit groups resolved in Phase 3.
        static bool IsPlaceholder(string n) => n == "human" || n == "player" || n.StartsWith("hildir:");

        [Theory]
        [InlineData("monsters.json")]
        [InlineData("items.json")]
        [InlineData("npcs.json")]
        public void EveryPrefabExists(string file)
        {
            var missing = PrefabNames(Load(file)).Where(n => n != null && !IsPlaceholder(n) && !Prefabs.Contains(n)).ToList();
            Assert.True(missing.Count == 0, $"{file}: unknown prefabs {string.Join(", ", missing)}");
        }

        [Fact]
        public void AllEquipmentIdsAreMapped()
        {
            var ids = Load("items.json").GetProperty("equipment").EnumerateArray().Select(e => e.GetProperty("id").GetInt32()).ToList();
            Assert.Equal(Enumerable.Range(0, 48), ids);
        }

        [Fact]
        public void EveryNamedMonsterTypeIsListedOnce()
        {
            var types = Load("monsters.json").GetProperty("creatures").EnumerateArray().Select(e => e.GetProperty("type").GetInt32()).ToList();
            Assert.Equal(types.Count, types.Distinct().Count());
            // 16..47 minus the two unnamed slots 42, 43
            Assert.Equal(Enumerable.Range(16, 32).Where(t => t != 42 && t != 43), types.OrderBy(t => t));
        }

        [Fact]
        public void TerrainTilesAreUnique()
        {
            var tiles = Load("terrain.json").GetProperty("classes").EnumerateObject()
                .SelectMany(c => c.Value.GetProperty("tiles").EnumerateArray().Select(t => t.GetInt32())).ToList();
            Assert.Equal(tiles.Count, tiles.Distinct().Count());
        }

        [U5Fact]
        public void EveryOverworldTileHasATerrainClass()
        {
            var classified = new HashSet<int>(Load("terrain.json").GetProperty("classes").EnumerateObject()
                .SelectMany(c => c.Value.GetProperty("tiles").EnumerateArray().Select(t => t.GetInt32())));
            var unclassified = Env.Data.Britannia.Tiles.Concat(Env.Data.Underworld.Tiles).Select(t => (int)t).Distinct()
                .Where(t => !classified.Contains(t)).OrderBy(t => t)
                .Select(t => $"0x{t:x2} {Env.Data.TileNames[t]}").ToList();
            Assert.True(unclassified.Count == 0, "unclassified: " + string.Join("; ", unclassified));
        }
    }
}
