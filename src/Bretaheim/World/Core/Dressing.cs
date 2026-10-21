using System;
using System.Collections.Generic;
using Bretaheim.Data;

namespace Bretaheim.WorldGen.Core
{
    public enum DressingEdge { Coast, River, Path }

    /// <summary>
    /// assets/mappings/dressing.json: what grows and lies along Britannia's edges (coasts, river
    /// banks, paths). Rules are tried in order for each 3 m cell; the first whose band, height and
    /// chance match picks one of its prefabs for the biome there. Deterministic per position.
    /// </summary>
    public class Dressing
    {
        public class Rule
        {
            public string Name;
            public DressingEdge Edge;
            /// <summary>Band in metres from the edge (coast: from the back of the beach sand).</summary>
            public float From, To;
            /// <summary>Chance per 3 m × 3 m cell.</summary>
            public float Chance;
            public float MinHeight = 0.3f, MaxHeight = 60f;
            public readonly Dictionary<VBiome, string[]> Prefabs = new Dictionary<VBiome, string[]>();
            public string[] AnyBiome;
        }

        public const float Cell = 3f;
        public readonly List<Rule> Rules = new List<Rule>();

        public static Dressing Parse(string json)
        {
            var d = new Dressing();
            foreach (var item in Json.Arr(Json.Obj(Json.Parse(json))["rules"]))
            {
                var o = Json.Obj(item);
                var r = new Rule
                {
                    Name = Json.Str(o, "name"),
                    Edge = (DressingEdge)Enum.Parse(typeof(DressingEdge), Json.Str(o, "edge"), true),
                    From = (float)Json.Num(o, "from"), To = (float)Json.Num(o, "to"),
                    Chance = (float)Json.Num(o, "chance"),
                    MinHeight = (float)Json.Num(o, "min_height", 0.3), MaxHeight = (float)Json.Num(o, "max_height", 60),
                };
                foreach (var kv in Json.Obj(o["prefabs"]))
                {
                    var list = Json.Arr(kv.Value).ConvertAll(x => (string)x).ToArray();
                    if (kv.Key == "*") r.AnyBiome = list;
                    else r.Prefabs[(VBiome)Enum.Parse(typeof(VBiome), kv.Key, true)] = list;
                }
                d.Rules.Add(r);
            }
            return d;
        }

        /// <summary>
        /// The prefab for the 3 m cell (cx, cz) (cell indices), or null; x/z/yaw are where it stands.
        /// Paths themselves and the water stay clear.
        /// </summary>
        public string Pick(BritanniaShape shape, int cx, int cz, out float x, out float z, out float height, out float yaw)
        {
            uint h = Hash(cx, cz, 0);
            x = (cx + (h & 0xff) / 256f) * Cell;
            z = (cz + ((h >> 8) & 0xff) / 256f) * Cell;
            yaw = (h >> 16) % 360;
            height = 0;
            float path = shape.PathEdgeDistance(x, z);
            if (path < 0.5f) return null;
            float coast = shape.CoastDistance(x, z, out float beach), river = float.NaN;
            VBiome biome = VBiome.None;
            bool haveHeight = false;
            for (int i = 0; i < Rules.Count; i++)
            {
                var r = Rules[i];
                float d;
                switch (r.Edge)
                {
                    case DressingEdge.Coast: if (coast <= 0) continue; d = coast - beach; break;
                    case DressingEdge.River: if (float.IsNaN(river)) river = shape.RiverDistance(x, z); d = river; break;
                    default: d = path; break;
                }
                if (!(d >= r.From && d < r.To)) continue;
                uint roll = Hash(cx, cz, 1 + i);
                if ((roll & 0xffff) / 65536f >= r.Chance) continue;
                if (!haveHeight) { height = shape.Height(x, z); haveHeight = true; }
                if (height < r.MinHeight || height > r.MaxHeight) continue;
                if (biome == VBiome.None) biome = shape.Biome(x, z);
                if (!r.Prefabs.TryGetValue(biome, out var choices)) choices = r.AnyBiome;
                if (choices == null || choices.Length == 0) continue;
                return choices[(roll >> 16) % (uint)choices.Length];
            }
            return null;
        }

        static uint Hash(int x, int z, int salt)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093 ^ z * 19349663 ^ salt * 83492791 ^ 0x5bd1e995);
                h = (h ^ (h >> 13)) * 1274126177u;
                h = (h ^ (h >> 15)) * 2246822519u;
                return h ^ (h >> 16);
            }
        }
    }
}
