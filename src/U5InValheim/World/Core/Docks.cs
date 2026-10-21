using System;

namespace U5InValheim.WorldGen.Core
{
    /// <summary>
    /// Where a vessel bought from a shipwright is delivered (SHOPPES2 shipwright_deliver: the
    /// dock cell g_dock_x/y of the shop, outside the town): the dock's overworld tile moved with
    /// its settlement (the squares are moved apart, <see cref="SiteLayout"/>), then the nearest
    /// water there deep and wide enough to float a ship, clear of the settlement's square.
    /// </summary>
    public static class Docks
    {
        /// <summary>Water this deep (metres under the sea's surface) floats a longship without grounding.</summary>
        public const float Depth = 2f;
        /// <summary>Clear water this far round the berth (a longship is about 20 m long).</summary>
        public const float Room = 9f;
        public const float Search = 160f, Step = 4f;

        /// <summary>
        /// The berth for a dock cell: world x/z, or false when there is no such water within
        /// <see cref="Search"/> metres. <paramref name="locX"/>/<paramref name="locY"/> is the
        /// settlement's own overworld tile and <paramref name="siteX"/>/<paramref name="siteZ"/> where its square
        /// stands; <paramref name="keepOut"/> is the half side of the square plus its slope.
        /// </summary>
        public static bool Berth(BritanniaShape shape, int dockX, int dockY, int locX, int locY, float siteX, float siteZ, float keepOut, out float x, out float z)
        {
            shape.TileCentre(dockX, dockY, out float dx, out float dz);
            shape.TileCentre(locX, locY, out float lx, out float lz);
            float tx = dx + (siteX - lx), tz = dz + (siteZ - lz);
            for (float r = 0; r <= Search; r += Step)
            {
                int around = r == 0 ? 1 : Math.Max(8, (int)(2 * Math.PI * r / Step));
                // (from the side facing away from the town first: the dock is outside it)
                double away = Math.Atan2(tz - siteZ, tx - siteX);
                for (int i = 0; i < around; i++)
                {
                    int k = (i + 1) / 2 * (i % 2 == 0 ? 1 : -1);
                    double a = away + k * 2 * Math.PI / around;
                    float px = tx + (float)(r * Math.Cos(a)), pz = tz + (float)(r * Math.Sin(a));
                    if (Math.Max(Math.Abs(px - siteX), Math.Abs(pz - siteZ)) < keepOut + Room) continue;
                    if (!Floats(shape, px, pz)) continue;
                    x = px;
                    z = pz;
                    return true;
                }
            }
            x = tx;
            z = tz;
            return false;
        }

        /// <summary>Deep water at a point and all round it within <see cref="Room"/>.</summary>
        public static bool Floats(BritanniaShape shape, float x, float z)
        {
            if (shape.Height(x, z) > -Depth) return false;
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4;
                if (shape.Height(x + (float)(Room * Math.Cos(a)), z + (float)(Room * Math.Sin(a))) > -Depth) return false;
            }
            return true;
        }
    }
}
