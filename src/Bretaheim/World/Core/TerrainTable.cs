using System;
using System.Collections.Generic;
using Bretaheim.Data;

namespace Bretaheim.WorldGen.Core
{
    /// <summary>Valheim's Heightmap.Biome flag values, mirrored so the core stays Unity-free.</summary>
    [Flags]
    public enum VBiome
    {
        None = 0, Meadows = 1, Swamp = 2, Mountain = 4, BlackForest = 8, Plains = 0x10,
        AshLands = 0x20, DeepNorth = 0x40, Ocean = 0x100, Mistlands = 0x200,
    }

    public enum WaterKind { None, Sea, River, Lava }

    /// <summary>One terrain class of assets/mappings/terrain.json.</summary>
    public class TerrainClass
    {
        public string Name;
        /// <summary>Land biome, or None when the class takes its neighbours' biome ("inherit", POIs, water).</summary>
        public VBiome Biome;
        public WaterKind Water;
        /// <summary>Elevation in metres relative to sea level, before noise.</summary>
        public float Base;
        /// <summary>Noise amplitude in metres.</summary>
        public float Noise;
        public string Poi;
        public bool Impassable;
        /// <summary>Valheim forest factor (lower = denser trees; ≥ 1.6 open, 3 treeless).</summary>
        public float Forest = 1f;
        /// <summary>Diagonal shore tiles: the half of the tile that is sea ("nw", "ne", "se", "sw"), else null.</summary>
        public string SeaHalf;
        public float SeaBase = -4f;
        /// <summary>Biome overrides within R tiles of a map position.</summary>
        public List<(int x, int y, int r, VBiome biome)> BiomeNear = new List<(int, int, int, VBiome)>();
    }

    /// <summary>Tile id (0..255) → terrain class, from terrain.json.</summary>
    public class TerrainTable
    {
        readonly TerrainClass[] byTile = new TerrainClass[256];
        public readonly Dictionary<string, TerrainClass> Classes = new Dictionary<string, TerrainClass>();

        public TerrainClass this[int tile] => byTile[tile & 0xff];

        /// <summary>
        /// The table of terrain.json; for the Underworld (UNDER.DAT) with its "underworld" changes
        /// applied (any of a class's fields but its tiles).
        /// </summary>
        public static TerrainTable Parse(string json, bool underworld = false)
        {
            var root = Json.Obj(Json.Parse(json));
            var t = new TerrainTable();
            var changes = underworld ? Json.Get(root, "underworld") as Dictionary<string, object> : null;
            foreach (var kv in Json.Obj(root["classes"]))
            {
                var o = Json.Obj(kv.Value);
                if (changes != null && Json.Get(changes, kv.Key) is Dictionary<string, object> change)
                {
                    o = new Dictionary<string, object>(o);
                    foreach (var f in change) o[f.Key] = f.Value;
                }
                string biome = Json.Str(o, "biome");
                string water = Json.Str(o, "water");
                var c = new TerrainClass
                {
                    Name = kv.Key,
                    Biome = biome == null || biome == "inherit" ? VBiome.None : (VBiome)Enum.Parse(typeof(VBiome), biome),
                    Water = water == null ? WaterKind.None
                        : water == "river" ? WaterKind.River
                        : water == "lava" ? WaterKind.Lava
                        : WaterKind.Sea,
                    Base = (float)Json.Num(o, "base"),
                    Noise = (float)Json.Num(o, "noise"),
                    Poi = Json.Str(o, "poi"),
                    Impassable = Json.Bool(o, "impassable"),
                    Forest = (float)Json.Num(o, "forest", 1),
                    SeaHalf = Json.Str(o, "sea_half"),
                    SeaBase = (float)Json.Num(o, "sea_base", -4),
                };
                if (Json.Get(o, "biome_near") is List<object> near)
                    foreach (var n in near)
                    {
                        var e = Json.Obj(n);
                        c.BiomeNear.Add(((int)Json.Num(e, "x"), (int)Json.Num(e, "y"), (int)Json.Num(e, "r"),
                            (VBiome)Enum.Parse(typeof(VBiome), Json.Str(e, "biome"))));
                    }
                // Sea water is the Ocean biome; the land biome of everything wet comes from its banks.
                if (c.Water == WaterKind.Sea) c.Biome = VBiome.None;
                t.Classes[c.Name] = c;
                foreach (var id in Json.Arr(o["tiles"])) t.byTile[(int)(double)id] = c;
            }
            string fallback = Json.Str(root, "unclassified") ?? "grass";
            for (int i = 0; i < 256; i++)
                if (t.byTile[i] == null) t.byTile[i] = t.Classes[fallback];
            return t;
        }
    }
}
