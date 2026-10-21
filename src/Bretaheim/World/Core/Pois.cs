using System.Collections.Generic;
using Bretaheim.Data;

namespace Bretaheim.WorldGen.Core
{
    public class Poi
    {
        public string Name;
        /// <summary>towne, village, keep, castle, dwelling, lighthouse, dungeon, shrine, codex, moongate.</summary>
        public string Kind;
        public int TileX, TileY;
        public bool Underworld;
        /// <summary>Location number 1..40 when the place has a map/NPCs, else 0.</summary>
        public int Location;

        public override string ToString() => $"{Name} ({Kind}) @({TileX},{TileY}){(Underworld ? " under" : "")}";
    }

    /// <summary>Every named place of both maps: the 40 locations, the 8 shrines, the Codex and the moongate sites.</summary>
    public static class Pois
    {
        public static List<Poi> Build(U5Data d)
        {
            var r = new List<Poi>();
            foreach (var l in d.Locations)
            {
                var world = l.InUnderworld ? d.Underworld : d.Britannia;
                r.Add(new Poi
                {
                    Name = l.Name,
                    Kind = KindOfTile(world[l.X, l.Y]),
                    TileX = l.X, TileY = l.Y,
                    Underworld = l.InUnderworld,
                    Location = l.Number,
                });
            }

            var virtues = d.Ovl.Virtues;
            for (int i = 0; i < 8; i++)
            {
                int sx = d.Ovl.U8(Dg.ShrineX + i), sy = d.Ovl.U8(Dg.ShrineY + i);
                // Spirituality's shrine is (0,0): it is not on the map, only reached by moongate.
                if (sx == 0 && sy == 0) continue;
                r.Add(new Poi
                {
                    Name = $"Shrine of {virtues[i]}",
                    Kind = "shrine",
                    TileX = sx,
                    TileY = sy,
                });
            }

            for (int y = 0; y < WorldMap.Size; y++)
            for (int x = 0; x < WorldMap.Size; x++)
            {
                byte t = d.Britannia[x, y];
                if (t == 0x11) r.Add(new Poi { Name = "Shrine of the Codex", Kind = "codex", TileX = x, TileY = y });
                else if (t == 0xa4) r.Add(new Poi { Name = "Moongate", Kind = "moongate", TileX = x, TileY = y });
            }
            return r;
        }

        static string KindOfTile(byte t)
        {
            switch (t)
            {
                case 0x10: return "dwelling";
                case 0x12: return "keep";
                case 0x13: return "village";
                case 0x14: return "towne";
                case 0x15: return "castle";
                case 0x16: case 0x17: case 0x18: return "dungeon";
                case 0x1b: return "lighthouse";
                default: return t >= 0x39 && t <= 0x3f ? "castle" : "place";
            }
        }
    }
}
