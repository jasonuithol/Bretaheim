using System;
using System.Collections.Generic;

namespace Bretaheim.WorldGen.Core
{
    /// <summary>
    /// The overworld's bridges (tiles 0x1d and 0x6a east–west, 0x6b north–south; MASTER_PLAN §5.1:
    /// "bridges are built from wood pieces (Valheim has no bridge asset)"). The river runs on under
    /// the bridge tile (terrain.json "bridge" is river water); the bridge crosses it along its path
    /// from dry bank to dry bank (the rivers are 50-85 m wide there): a plank deck 4 m wide a little above the banks,
    /// a rail on posts each side, and log piers down to the river bed. Where a bank is lower
    /// than the deck, a sloping plank ramp comes down to it. Unity-free: pieces in world x/z, y in
    /// metres above the sea.
    /// </summary>
    public static class BridgePlanner
    {
        public const float Width = 4f, Segment = 4f;
        /// <summary>The deck's top stands this far over the higher of its two banks, and never lower than <see cref="MinDeck"/> over the water.</summary>
        public const float OverBank = 0.3f, MinDeck = 1.6f;
        /// <summary>Ground at least this high is a bank to stand the bridge on; the bridge reaches at most this far from the tile's middle.</summary>
        public const float Dry = 0.05f, Reach = 40f;
        /// <summary>A ramp's slope at most (degrees).</summary>
        public const float RampSlope = 18f;

        /// <summary>The bridge tiles of a map: (x, y, runs east–west).</summary>
        public static IEnumerable<(int x, int y, bool eastWest)> Bridges(BritanniaShape shape)
        {
            for (int y = 0; y < BritanniaShape.MapTiles; y++)
                for (int x = 0; x < BritanniaShape.MapTiles; x++)
                {
                    byte t = shape.Map[x, y];
                    if (t == 0x1d || t == 0x6a) yield return (x, y, true);
                    else if (t == 0x6b) yield return (x, y, false);
                }
        }

        /// <summary>The pieces of the bridge on tile (tx, ty).</summary>
        public static List<Placement> Plan(BritanniaShape shape, int tx, int ty, bool eastWest)
        {
            shape.TileCentre(tx, ty, out float cx, out float cz);
            float ax = eastWest ? 1 : 0, az = eastWest ? 0 : 1;
            float Ground(float s) => shape.Height(cx + ax * s, cz + az * s);
            // the banks: the first dry ground each way from the middle
            float Bank(int dir)
            {
                for (float s = 0; s <= Reach; s += 0.5f)
                    if (Ground(dir * s) >= Dry) return dir * s;
                return dir * Reach;
            }
            float a = Bank(-1), b = Bank(1);
            float deck = Math.Max(MinDeck, Math.Max(Ground(a), Ground(b)) + OverBank);
            // the deck reaches 2 m onto each bank, in whole segments
            float start = a - 2, end = b + 2;
            int count = Math.Max(1, (int)Math.Ceiling((end - start) / Segment));
            float seg = (end - start) / count, mid = (start + end) / 2;
            float yaw = eastWest ? 0 : 90;
            var r = new List<Placement>();
            void Add(string prefab, float s, float side, float y, float pyaw, float sx, float sy, float sz, float tilt = 0)
            {
                // s along the bridge, side across it (to the left looking along +s: north of an east–west bridge)
                float x = cx + ax * s - az * side, z = cz + az * s + ax * side;
                var p = new Placement { Prefab = prefab, X = x, Y = y, Z = z, Yaw = pyaw, Tilt = tilt };
                if (sx != 1 || sy != 1 || sz != 1) { p.SX = sx; p.SY = sy; p.SZ = sz; }
                r.Add(p);
            }
            for (int i = 0; i < count; i++)
            {
                float s = start + (i + 0.5f) * seg;
                // wood_floor: 2 x 2 m, its top 0.097 m over its pivot
                Add("wood_floor", s, 0, deck - 0.097f, yaw, seg / 2, 1, Width / 2);
                // the rails: a post at each segment's start (and the last one's end), a beam along on top
                foreach (float side in new[] { -Width / 2 + 0.1f, Width / 2 - 0.1f })
                {
                    Add("wood_pole2", s - seg / 2, side, deck + 0.55f, 0, 0.5f, 0.55f, 0.5f);
                    if (i == count - 1) Add("wood_pole2", s + seg / 2, side, deck + 0.55f, 0, 0.5f, 0.55f, 0.5f);
                    Add("wood_beam", s, side, deck + 1.05f, yaw, seg / 2, 0.4f, 0.4f);
                }
                // piers where the river is under it (every other segment): a log each side, down into the bed
                float bed = Ground(s);
                if (bed < -0.3f && i % 2 == 0)
                    foreach (float side in new[] { -Width / 2 + 0.3f, Width / 2 - 0.3f })
                    {
                        float top = deck - 0.13f, bottom = bed - 0.8f, length = top - bottom;
                        Add("wood_pole_log_4", s, side, (top + bottom) / 2, 0, 1, length / 4, 1);
                    }
            }
            // ramps down to a bank lower than the deck (beyond each end)
            foreach (int dir in new[] { -1, 1 })
            {
                float at = dir < 0 ? start : end;
                float drop = deck - Ground(at);
                if (drop < 0.3f) continue;
                float run = drop / (float)Math.Tan(RampSlope * Math.PI / 180), length = (float)Math.Sqrt(run * run + drop * drop);
                float tilt = (float)(Math.Atan2(drop, run) * 180 / Math.PI);
                // a plank floor turned up its slope (tilt about its own east–west axis; the ramp along its local z)
                float rampYaw = eastWest ? (dir > 0 ? 90 : 270) : (dir > 0 ? 0 : 180);
                Add("wood_floor", at + dir * run / 2, 0, deck - drop / 2 - 0.097f, rampYaw, Width / 2, 1, length / 2, tilt);
            }
            return r;
        }
    }
}
