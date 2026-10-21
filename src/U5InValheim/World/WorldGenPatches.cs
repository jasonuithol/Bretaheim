using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// Replaces Valheim's terrain with Britannia while a Britannia world is loaded. Targets are
    /// looked up by name and checked, so a Valheim update that renames one disables the world
    /// generator cleanly (vanilla terrain, error in the log) instead of half-patching it — the
    /// DayNightRobust approach. Confirmed against the decompiled assembly_valheim (2026-10):
    /// GetBiome / GetBiomeHeight are the only sources the terrain builder, minimap, biome-data
    /// grid (AltBiomeWorldData) and location placement read.
    /// </summary>
    public static class WorldGenPatches
    {
        static Harmony harmony;
        public static bool Installed { get; private set; }

        public static void Install(Harmony h)
        {
            harmony = h;
            try
            {
                Patch(typeof(WorldGenerator), "Initialize", new[] { typeof(global::World) }, prefix: nameof(Initialize_Prefix));
                Patch(typeof(WorldGenerator), "Pregenerate", Type.EmptyTypes, prefix: nameof(Pregenerate_Prefix));
                Patch(typeof(WorldGenerator), "GetBiome", new[] { typeof(float), typeof(float), typeof(float), typeof(bool) }, prefix: nameof(GetBiome_Prefix));
                Patch(typeof(WorldGenerator), "GetBiomeHeight",
                    new[] { typeof(Heightmap.Biome), typeof(float), typeof(float), typeof(Color).MakeByRefType(), typeof(bool), typeof(bool) },
                    prefix: nameof(GetBiomeHeight_Prefix));
                Patch(typeof(global::World), "GenerateSeed", Type.EmptyTypes, postfix: nameof(GenerateSeed_Postfix));
                Patch(typeof(ZoneSystem), "Update", Type.EmptyTypes, prefix: nameof(ZoneUpdate_Prefix));
                Patch(typeof(ZoneSystem), "GenerateLocationsTimeSliced",
                    new[] { typeof(ZoneSystem.ZoneLocation), typeof(Stopwatch), typeof(ZPackage) }, prefix: nameof(GenerateLocation_Prefix));
                Installed = true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"World generator not installed, Valheim has changed: {e.Message}");
                harmony.UnpatchSelf();
                Installed = false;
                return;
            }
            // tree density per tile (optional: without it the vanilla forests grow)
            try
            {
                Patch(typeof(WorldGenerator), "GetForestFactor", new[] { typeof(Vector3) }, prefix: nameof(GetForestFactor_Prefix));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Forest density per tile not installed: {e.Message}");
            }
        }

        static void Patch(Type type, string method, Type[] args, string prefix = null, string postfix = null)
        {
            var m = AccessTools.Method(type, method, args) ?? throw new MissingMethodException(type.Name, method);
            harmony.Patch(m,
                prefix == null ? null : new HarmonyMethod(typeof(WorldGenPatches), prefix),
                postfix == null ? null : new HarmonyMethod(typeof(WorldGenPatches), postfix));
        }

        // ── terrain ─────────────────────────────────────────────────────────────────────────

        static void Initialize_Prefix(global::World world) => Britannia.Activate(world);

        /// <summary>Valheim's rivers and lakes are not used: U5's rivers are part of the map.</summary>
        static bool Pregenerate_Prefix(ref List<Vector2> ___m_lakes)
        {
            if (Britannia.Active == null) return true;
            ___m_lakes = new List<Vector2>();
            return false;
        }

        static bool GetForestFactor_Prefix(Vector3 pos, ref float __result)
        {
            if (Britannia.Active == null) return true;
            __result = Britannia.ShapeAt(pos.x, pos.z).ForestFactor(pos.x, pos.z);
            return false;
        }

        static bool GetBiome_Prefix(float wx, float wy, ref Heightmap.Biome __result)
        {
            if (Britannia.Active == null) return true;
            __result = (Heightmap.Biome)(int)Britannia.ShapeAt(wx, wy).Biome(wx, wy);
            return false;
        }

        /// <summary>One continuous height for every biome argument: the terrain builder blends the
        /// heights of a zone's corner biomes, and they must agree for seamless borders. Both
        /// continents (Britannia, and the Underworld east of it) are sampled here.</summary>
        static bool GetBiomeHeight_Prefix(float wx, float wy, ref Color mask, ref float __result)
        {
            if (Britannia.Active == null) return true;
            var shape = Britannia.ShapeAt(wx, wy);
            mask = PaintColour(shape.PaintAt(wx, wy));
            // Valheim's lava is Ashlands ground whose paint alpha is high, and every paint colour
            // has alpha 1: Britannia's deserts (Ashlands) would be molten. In Ashlands the alpha is
            // the map's own lava (the Underworld's lava tiles; nothing on the surface's sand)
            if (shape.Biome(wx, wy) == Core.VBiome.AshLands) mask.a = shape.Lava(wx, wy);
            __result = Britannia.SeaLevel + shape.Height(wx, wy);
            return false;
        }

        /// <summary>
        /// The terrain builder uses this mask as the ground's paint (Heightmap m_paintMask): red dirt,
        /// green cultivated, blue paved, black nothing.
        /// </summary>
        static Color PaintColour(Core.Paint paint)
        {
            switch (paint)
            {
                case Core.Paint.Dirt: return Heightmap.m_paintMaskDirt;
                case Core.Paint.Cultivated: return Heightmap.m_paintMaskCultivated;
                case Core.Paint.Paved: return Heightmap.m_paintMaskPaved;
                default: return Color.black;
            }
        }

        /// <summary>New worlds are Britannia unless the player types another seed.</summary>
        static void GenerateSeed_Postfix(ref string __result)
        {
            if (!Plugin.NewWorldsAreBritannia.Value || Plugin.U5 == null) return;
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            __result = Britannia.SeedPrefix + chars[UnityEngine.Random.Range(0, chars.Length)];
        }

        // ── locations ───────────────────────────────────────────────────────────────────────

        static MethodInfo registerLocation;

        /// <summary>
        /// Vanilla places (altars, crypts, traders, villages …) are not generated in Britannia.
        /// The start temple — Valheim's spawn point — is registered at Iolo's hut instead (or outside the
        /// settlement of the SpawnLocation setting).
        /// </summary>
        static bool GenerateLocation_Prefix(ZoneSystem __instance, ZoneSystem.ZoneLocation location, ZPackage iterationsPkg, ref IEnumerator __result)
        {
            if (Britannia.Active == null && Britannia.Failure == null) return true;
            // The caller reads the number of placement attempts back from the package.
            iterationsPkg.Write(0);
            iterationsPkg.SetPos(0);
            __result = Empty();
            if (Britannia.Active == null) return false;
            if (location == null || Game.instance == null || location.m_prefab.Name != Game.instance.m_StartLocation) return false;

            var existing = new List<ZoneSystem.LocationInstance>();
            if (__instance.FindLocations(location.m_prefab.Name, ref existing) && existing.Count > 0) return false;
            var pos = Britannia.SpawnPoint();
            // With the spawn settlement built (Iolo's hut, or the place of SpawnLocation), the temple
            // itself is never spawned (registered as placed): its registration only marks where
            // players appear.
            bool hutBuilt = Settlements.IsBuilt(Plugin.SpawnLocation.Value);
            registerLocation = registerLocation ?? AccessTools.Method(typeof(ZoneSystem), "RegisterLocation");
            registerLocation.Invoke(__instance, new object[] { location, pos, hutBuilt });
            Plugin.Log.LogInfo($"Start location placed at {(Settlements.IsBuilt(Plugin.SpawnLocation.Value) ? Plugin.U5.Location(Plugin.SpawnLocation.Value).Name : "the start tile")} {pos} (temple {(hutBuilt ? "not spawned" : "spawned")}, " +
                               $"iconAlways {location.m_iconAlways}, iconPlaced {location.m_iconPlaced})");
            return false;
        }

        static float nextWarning;

        /// <summary>A Britannia world that failed to build generates no zones (see Britannia.Failure).</summary>
        static bool ZoneUpdate_Prefix()
        {
            if (Britannia.Failure == null) return true;
            if (Time.time >= nextWarning && MessageHud.instance != null)
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "U5InValheim: " + Britannia.Failure);
                nextWarning = Time.time + 10f;
            }
            return false;
        }

        static IEnumerator Empty()
        {
            yield break;
        }
    }
}
