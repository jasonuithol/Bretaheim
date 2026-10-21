using System;
using System.Collections.Generic;
using Bretaheim.Data;

namespace Bretaheim.WorldGen.Core
{
    /// <summary>
    /// Collider bounds of Valheim prefabs relative to their pivot, dumped from the real prefabs by
    /// DevTools.PieceGeometryDump (assets/mappings/piece_geometry.json). Lets the planner place a
    /// piece by where its body should be rather than by its (prefab-specific) pivot.
    /// </summary>
    public class PieceGeometry
    {
        public struct Box
        {
            public float MinX, MinY, MinZ, MaxX, MaxY, MaxZ;
            public float SizeX => MaxX - MinX;
            public float SizeY => MaxY - MinY;
            public float SizeZ => MaxZ - MinZ;
        }

        readonly Dictionary<string, Box> boxes = new Dictionary<string, Box>();

        public static PieceGeometry Parse(string json)
        {
            var g = new PieceGeometry();
            foreach (var kv in Json.Obj(Json.Parse(json)))
            {
                var o = Json.Obj(kv.Value);
                if (!(Json.Get(o, "min") is List<object> mn) || !(Json.Get(o, "max") is List<object> mx)) continue;
                g.boxes[kv.Key] = new Box
                {
                    MinX = (float)(double)mn[0], MinY = (float)(double)mn[1], MinZ = (float)(double)mn[2],
                    MaxX = (float)(double)mx[0], MaxY = (float)(double)mx[1], MaxZ = (float)(double)mx[2],
                };
            }
            return g;
        }

        public bool Has(string prefab) => boxes.ContainsKey(prefab);

        public Box this[string prefab]
            => boxes.TryGetValue(prefab, out var b) ? b : throw new KeyNotFoundException($"no geometry for prefab '{prefab}'");

        public bool TryGet(string prefab, out Box box) => boxes.TryGetValue(prefab, out box);
    }
}
