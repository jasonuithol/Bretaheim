using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Bretaheim.WorldGen
{
    /// <summary>
    /// Soft biome borders. Valheim colours a 64 m terrain chunk from the biomes at its four corners
    /// and answers "which biome is here" from the nearest corner, which is fine for vanilla's huge
    /// biomes but turns Britannia's 32 m tiles into 64 m squares with straight edges. In a
    /// Britannia world each chunk's ground is coloured from the biome map itself (sampled every
    /// 4 m and blurred over ~8 m), and the biome of a point is the map's own answer, so plants
    /// and ground follow the same irregular borders.
    /// </summary>
    public static class BiomeBlend
    {
        const float Step = 4f;
        const int Radius = 2;
        static readonly List<Color32> colours = new List<Color32>();

        public static void Install(Harmony harmony)
        {
            try
            {
                var rebuild = AccessTools.Method(typeof(Heightmap), "RebuildRenderMesh") ?? throw new MissingMethodException("Heightmap", "RebuildRenderMesh");
                var biome = AccessTools.Method(typeof(Heightmap), "GetBiome", new[] { typeof(Vector3), typeof(float), typeof(bool) })
                            ?? throw new MissingMethodException("Heightmap", "GetBiome");
                harmony.Patch(rebuild, postfix: new HarmonyMethod(typeof(BiomeBlend), nameof(RebuildRenderMesh_Postfix)));
                harmony.Patch(biome, prefix: new HarmonyMethod(typeof(BiomeBlend), nameof(GetBiome_Prefix)));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Soft biome borders not installed: {e.Message}");
            }
        }

        static bool GetBiome_Prefix(Vector3 point, ref Heightmap.Biome __result)
        {
            if (Britannia.Active == null) return true;
            __result = (Heightmap.Biome)(int)Britannia.ShapeAt(point.x, point.z).Biome(point.x, point.z);
            return false;
        }

        static void RebuildRenderMesh_Postfix(Heightmap __instance, Mesh ___m_renderMesh, int ___m_width, float ___m_scale, bool ___m_isDistantLod)
        {
            if (Britannia.Active == null || ___m_isDistantLod || ___m_renderMesh == null) return;
            var shape = Britannia.ShapeAt(__instance.transform.position);
            float span = ___m_width * ___m_scale;
            Vector3 origin = __instance.transform.position - new Vector3(span / 2, 0, span / 2);

            // biome colours on a 4 m grid reaching Radius cells past the chunk, box-blurred
            int g = Mathf.CeilToInt(span / Step) + 1 + 2 * Radius;
            var grid = new Color[g * g];
            for (int gy = 0; gy < g; gy++)
            for (int gx = 0; gx < g; gx++)
            {
                var b = (Heightmap.Biome)(int)shape.Biome(origin.x + (gx - Radius) * Step, origin.z + (gy - Radius) * Step);
                grid[gy * g + gx] = Heightmap.GetBiomeColor(b);
            }
            grid = Box(Box(grid, g, 1, 0), g, 0, 1);

            int n = ___m_width + 1;
            colours.Clear();
            for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                float fx = j * ___m_scale / Step + Radius, fy = i * ___m_scale / Step + Radius;
                int x0 = Mathf.Min((int)fx, g - 2), y0 = Mathf.Min((int)fy, g - 2);
                float tx = fx - x0, ty = fy - y0;
                var a = Color.Lerp(grid[y0 * g + x0], grid[y0 * g + x0 + 1], tx);
                var c = Color.Lerp(grid[(y0 + 1) * g + x0], grid[(y0 + 1) * g + x0 + 1], tx);
                colours.Add(Color.Lerp(a, c, ty));
            }
            if (colours.Count == ___m_renderMesh.vertexCount) ___m_renderMesh.SetColors(colours);
        }

        /// <summary>A box blur of radius Radius along (dx, dy), clamped at the grid's edge.</summary>
        static Color[] Box(Color[] src, int g, int dx, int dy)
        {
            var dst = new Color[src.Length];
            for (int y = 0; y < g; y++)
            for (int x = 0; x < g; x++)
            {
                Color sum = Color.clear;
                for (int k = -Radius; k <= Radius; k++)
                {
                    int sx = Mathf.Clamp(x + k * dx, 0, g - 1), sy = Mathf.Clamp(y + k * dy, 0, g - 1);
                    sum += src[sy * g + sx];
                }
                dst[y * g + x] = sum / (2 * Radius + 1);
            }
            return dst;
        }
    }
}
