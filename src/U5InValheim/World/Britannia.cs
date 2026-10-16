using System;
using System.Collections.Generic;
using System.IO;
using U5InValheim.WorldGen.Core;
using UnityEngine;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// Which worlds are Britannia and the shape of the active one. A world is Britannia when its
    /// seed name starts with "Britannia" (case-insensitive): the seed name is sent to every client
    /// with the world, so clients agree with the server without any extra message, and worlds made
    /// before the mod was installed are left alone. With NewWorldsAreBritannia (default on) new
    /// worlds get such a seed (see <see cref="WorldGenPatches"/>).
    /// </summary>
    public static class Britannia
    {
        public const string SeedPrefix = "Britannia";
        public const float SeaLevel = 30f;

        /// <summary>The surface shape while a Britannia world is loaded, else null.</summary>
        public static BritanniaShape Active { get; private set; }

        /// <summary>
        /// The loaded world is Britannia but could not be built. Zone and location generation are
        /// then stopped (<see cref="WorldGenPatches"/>): generating vanilla terrain would be saved
        /// into the Britannia world.
        /// </summary>
        public static string Failure { get; private set; }
        public static List<Poi> Pois { get; private set; } = new List<Poi>();

        static BritanniaShape cached;
        static int cachedSeed;
        static float cachedTile;
        static TerrainTable terrain;

        public static bool IsBritanniaSeed(string seedName)
            => seedName != null && seedName.StartsWith(SeedPrefix, StringComparison.OrdinalIgnoreCase);

        /// <summary>Called as WorldGenerator.Initialize(world) begins, before any terrain is sampled.</summary>
        public static void Activate(global::World world)
        {
            Active = null;
            Failure = null;
            if (world == null || world.m_menu || !IsBritanniaSeed(world.m_seedName)) return;
            if (Plugin.U5 == null)
            {
                Fail($"World '{world.m_name}' is a Britannia world but the Ultima V data is not loaded " +
                     "(set UltimaVDirectory and restart).");
                return;
            }
            try
            {
                float tile = Plugin.TileMetres.Value;
                if (cached == null || cachedSeed != world.m_seed || cachedTile != tile)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    terrain = terrain ?? TerrainTable.Parse(File.ReadAllText(Path.Combine(Plugin.PluginDirectory, "mappings", "terrain.json")));
                    cached = new BritanniaShape(Plugin.U5.Britannia, terrain, world.m_seed, tile);
                    cachedSeed = world.m_seed;
                    cachedTile = tile;
                    Plugin.Log.LogInfo($"Britannia terrain for seed '{world.m_seedName}' built in {sw.ElapsedMilliseconds} ms");
                }
                Pois = Core.Pois.Build(Plugin.U5);
                Settlements.Prepare(cached, Pois);
                PlaceMarkers.Reset();
                Active = cached;
                Plugin.Log.LogInfo($"World '{world.m_name}' is Britannia ({Pois.Count} places).");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(e.ToString());
                Fail($"Could not build Britannia for '{world.m_name}': {e.Message}");
            }
        }

        static void Fail(string why)
        {
            Active = null;
            Failure = why + " World generation is stopped so nothing vanilla is saved into it: quit Valheim completely and restart.";
            Plugin.Log.LogError(Failure);
        }

        /// <summary>World position (y = ground) of the centre of overworld tile (tx, ty).</summary>
        public static Vector3 TileCentre(int tx, int ty)
        {
            Active.TileCentre(tx, ty, out float wx, out float wz);
            return new Vector3(wx, SeaLevel + Active.Height(wx, wz), wz);
        }

        /// <summary>Where new players appear: inside Iolo's hut (INIT.GAM starts the party there).</summary>
        public static Vector3 SpawnPoint()
        {
            if (Settlements.TrySpawnPoint(out var inHut)) return inHut;
            var g = Plugin.U5.InitialGame;
            var loc = Plugin.U5.Location(g.Location >= 1 && g.Location <= 40 ? g.Location : 13);
            return TileCentre(loc.X, loc.Y);
        }
    }
}
