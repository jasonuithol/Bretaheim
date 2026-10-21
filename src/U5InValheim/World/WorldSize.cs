using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Emit;
using HarmonyLib;
using U5InValheim.WorldGen.Core;
using UnityEngine;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// The world-radius expansion for the Underworld (MASTER_PLAN §5.1, the "Expand World Size"
    /// technique), in Britannia worlds only. Valheim's world ends 10.5 km from its centre; the
    /// Underworld lies 9.6 km east of Britannia and reaches 14.4 km (<see cref="Continents"/>).
    /// Our terrain already answers everywhere (WorldGenPatches), so what is left of the edge is:
    /// the player pushed out and killed past 10.42 km (Player.EdgeOfWorldKill), ships pushed out
    /// (Ship.ApplyEdgeForce), the water's surface dropped 100 m past 10.5 km
    /// (WaterVolume.GetWaterSurface), the wind turned outwards near the edge (EnvMan.UpdateWind),
    /// the Ashlands and Deep North that begin 12 km out (WorldGenerator.IsAshlands, IsDeepnorth,
    /// their ocean gradient and wave fade) and the minimap, which covers ±12.3 km (its pixel size
    /// is raised to cover both continents). Each patch is optional: one that no longer finds its
    /// target logs a warning and the rest still work.
    /// </summary>
    public static class WorldSize
    {
        static Harmony harmony;
        static float originalPixelSize;

        /// <summary>The expansion applies: a Britannia world with its Underworld.</summary>
        public static bool Expanded => Britannia.Active != null && Britannia.Under != null;

        /// <summary>Where the ocean ends (Valheim's 10.5 km, or nowhere in Britannia).</summary>
        public static float WaterEdge() => Expanded ? 1e7f : Continents.ValheimEdge;

        public static void Install(Harmony h)
        {
            harmony = h;
            Patch(typeof(Player), "EdgeOfWorldKill", new[] { typeof(float) }, prefix: nameof(Skip));
            Patch(typeof(Ship), "ApplyEdgeForce", new[] { typeof(float) }, prefix: nameof(Skip));
            Patch(typeof(WaterVolume), "GetWaterSurface", new[] { typeof(Vector3), typeof(float) }, postfix: nameof(GetWaterSurface_Postfix));
            Patch(typeof(EnvMan), "UpdateWind", new[] { typeof(long), typeof(float) }, transpiler: nameof(Edge_Transpiler));
            Patch(typeof(WorldGenerator), "IsAshlands", new[] { typeof(float), typeof(float) }, prefix: nameof(IsAshlands_Prefix));
            Patch(typeof(WorldGenerator), "IsDeepnorth", new[] { typeof(float), typeof(float) }, prefix: nameof(IsDeepnorth_Prefix));
            Patch(typeof(WorldGenerator), "GetAshlandsOceanGradient", new[] { typeof(float), typeof(float) }, prefix: nameof(AshlandsOceanGradient_Prefix));
            Patch(typeof(WorldGenerator), "DeepNorthWaveFade", new[] { typeof(float), typeof(float) }, prefix: nameof(DeepNorthWaveFade_Prefix));
            Patch(typeof(Minimap), "Update", Type.EmptyTypes, prefix: nameof(MinimapUpdate_Prefix));
            Patch(typeof(Minimap), "TryLoadMinimapTextureData", new[] { typeof(int) }, prefix: nameof(TryLoadMinimap_Prefix));
            Patch(typeof(Minimap), "GenerateWorldMap", Type.EmptyTypes, postfix: nameof(GenerateWorldMap_Postfix));
        }

        static void Patch(Type type, string method, Type[] args, string prefix = null, string postfix = null, string transpiler = null)
        {
            var m = AccessTools.Method(type, method, args);
            if (m == null)
            {
                Plugin.Log.LogWarning($"World size: {type.Name}.{method} not found; the Underworld may meet Valheim's world edge there");
                return;
            }
            try
            {
                harmony.Patch(m,
                    prefix == null ? null : new HarmonyMethod(typeof(WorldSize), prefix),
                    postfix == null ? null : new HarmonyMethod(typeof(WorldSize), postfix),
                    transpiler == null ? null : new HarmonyMethod(typeof(WorldSize), transpiler));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"World size: {type.Name}.{method} not patched: {e.Message}");
            }
        }

        /// <summary>No world edge in Britannia: the edge force and the edge death are skipped.</summary>
        static bool Skip() => !Expanded;

        static void GetWaterSurface_Postfix(Vector3 point, float ___m_forceDepth, ref float __result)
        {
            // (past 10.5 km Valheim drops the sea 100 m: the world's edge waterfall)
            if (Expanded && ___m_forceDepth < 0f && Utils.LengthXZ(point) > Continents.ValheimEdge) __result += 100f;
        }

        /// <summary>Every 10500 (Valheim's water edge) in the method becomes <see cref="WaterEdge"/>.</summary>
        static IEnumerable<CodeInstruction> Edge_Transpiler(IEnumerable<CodeInstruction> code)
        {
            var edge = AccessTools.Method(typeof(WorldSize), nameof(WaterEdge));
            int n = 0;
            foreach (var c in code)
            {
                if (c.opcode == OpCodes.Ldc_R4 && c.operand is float f && f == Continents.ValheimEdge)
                {
                    // (changed in place, so any label on it stays)
                    n++;
                    c.opcode = OpCodes.Call;
                    c.operand = edge;
                }
                yield return c;
            }
            if (n == 0) Plugin.Log.LogWarning("World size: EnvMan.UpdateWind has no 10500 any more (the edge wind is left alone)");
        }

        /// <summary>
        /// Valheim's Ashlands begin 12 km south of its centre: in Britannia only the Underworld's
        /// lava is Ashlands (so it burns), and nowhere is Deep North.
        /// </summary>
        static bool IsAshlands_Prefix(float x, float y, ref bool __result)
        {
            if (Britannia.Active == null) return true;
            var under = Britannia.Under;
            __result = under != null && Continents.InUnderworld(x, under.TileMetres) && under.Biome(x, y) == VBiome.AshLands;
            return false;
        }

        static bool IsDeepnorth_Prefix(ref bool __result)
        {
            if (Britannia.Active == null) return true;
            __result = false;
            return false;
        }

        /// <summary>How far into the Ashlands' boiling sea (negative: not there). Britannia's sea never boils.</summary>
        static bool AshlandsOceanGradient_Prefix(ref float __result)
        {
            if (Britannia.Active == null) return true;
            __result = -20f;
            return false;
        }

        static bool DeepNorthWaveFade_Prefix(ref double __result)
        {
            if (Britannia.Active == null) return true;
            __result = 0;
            return false;
        }

        // ── the minimap ─────────────────────────────────────────────────────────────────────

        /// <summary>Half the side of the square both continents need on the map, metres.</summary>
        static float MapHalf()
        {
            var u = Britannia.Under;
            return u.OriginX + (BritanniaShape.MapTiles / 2f + u.MarginTiles + u.FadeTiles) * u.TileMetres;
        }

        /// <summary>Before the map is made (or read back): its pixels made big enough to hold the Underworld.</summary>
        static void MinimapUpdate_Prefix(Minimap __instance)
        {
            if (originalPixelSize <= 0) originalPixelSize = __instance.m_pixelSize;
            float want = Expanded ? Mathf.Max(originalPixelSize, Mathf.Ceil(MapHalf() * 2f / __instance.m_textureSize * 10f) / 10f) : originalPixelSize;
            if (__instance.m_pixelSize != want)
            {
                __instance.m_pixelSize = want;
                Plugin.Log.LogInfo($"Minimap: {want} m a pixel ({want * __instance.m_textureSize / 2000f:0.0} km either way of the centre)");
            }
        }

        /// <summary>The map cached on disk is used only if it was made at this pixel size by this version (a mark beside Valheim's own).</summary>
        static bool TryLoadMinimap_Prefix(Minimap __instance, string ___m_cachedMinimapMetaPath, ref bool __result)
        {
            if (!Expanded || string.IsNullOrEmpty(___m_cachedMinimapMetaPath)) return true;
            string mark = null;
            try { mark = File.Exists(___m_cachedMinimapMetaPath + ".u5") ? File.ReadAllText(___m_cachedMinimapMetaPath + ".u5") : null; }
            catch (Exception) { }
            if (mark == Signature(__instance)) return true;
            Plugin.Log.LogInfo("Minimap: the cached map is not this one (Britannia and the Underworld); making it again");
            __result = false;
            return false;
        }

        static void GenerateWorldMap_Postfix(Minimap __instance, string ___m_cachedMinimapMetaPath)
        {
            if (!Expanded || string.IsNullOrEmpty(___m_cachedMinimapMetaPath)) return;
            try { File.WriteAllText(___m_cachedMinimapMetaPath + ".u5", Signature(__instance)); }
            catch (Exception e) { Plugin.Log.LogWarning($"Minimap: could not mark the cached map: {e.Message}"); }
        }

        static string Signature(Minimap m) => $"U5InValheim {Plugin.PluginVersion} pixel {m.m_pixelSize:0.###}";
    }
}
