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
        /// <summary>The loaded Britannia world's seed (World.m_seed; the Shadowlords' travels are drawn from it).</summary>
        public static int Seed { get; private set; }

        /// <summary>
        /// The Underworld's shape while a Britannia world is loaded (UNDER.DAT, a second continent
        /// east of Britannia: <see cref="Continents"/>), else null.
        /// </summary>
        public static BritanniaShape Under { get; private set; }

        static BritanniaShape cached, cachedUnder;
        static int cachedSeed;
        static float cachedTile;
        static TerrainTable terrain, underTerrain;

        /// <summary>The continent a world position belongs to: the Underworld east of the midline, else Britannia.</summary>
        public static BritanniaShape ShapeAt(float wx, float wz)
            => Under != null && Continents.InUnderworld(wx, Under.TileMetres) ? Under : Active;

        public static BritanniaShape ShapeAt(Vector3 p) => ShapeAt(p.x, p.z);

        /// <summary>A position on the Underworld continent (not in a dungeon or interior far above it).</summary>
        public static bool InUnderworld(Vector3 p) => Active != null && ShapeAt(p.x, p.z) == Under && Under != null;

        /// <summary>The shape of the surface (z 0) or of the Underworld (z ≠ 0, the original's 0xff).</summary>
        public static BritanniaShape Level(int z) => z == 0 ? Active : Under;

        /// <summary>World position (y = ground) of the centre of tile (tx, ty) of the surface or the Underworld.</summary>
        public static Vector3 TileCentre(int tx, int ty, bool underworld)
        {
            var s = underworld ? Under : Active;
            s.TileCentre(tx, ty, out float wx, out float wz);
            return new Vector3(wx, SeaLevel + s.Height(wx, wz), wz);
        }

        /// <summary>The original's tile (0..255) and level (0 surface, −1 Underworld) of a world position.</summary>
        public static (int x, int y, int z) Tile(Vector3 p)
        {
            var s = ShapeAt(p);
            s.ToTile(p.x, p.z, out double u, out double v);
            return ((int)Math.Floor(u) & 0xff, (int)Math.Floor(v) & 0xff, s == Under ? -1 : 0);
        }

        public static bool IsBritanniaSeed(string seedName)
            => seedName != null && seedName.StartsWith(SeedPrefix, StringComparison.OrdinalIgnoreCase);

        /// <summary>Called as WorldGenerator.Initialize(world) begins, before any terrain is sampled.</summary>
        public static void Activate(global::World world)
        {
            ActivateWorld(world);
            RiverBanks.WorldChanged();
        }

        static void ActivateWorld(global::World world)
        {
            Active = null;
            Under = null;
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
                    string json = File.ReadAllText(Path.Combine(Plugin.PluginDirectory, "mappings", "terrain.json"));
                    terrain = terrain ?? TerrainTable.Parse(json);
                    underTerrain = underTerrain ?? TerrainTable.Parse(json, underworld: true);
                    cached = new BritanniaShape(Plugin.U5.Britannia, terrain, world.m_seed, tile);
                    cachedUnder = Continents.UnderworldShape(Plugin.U5.Underworld, underTerrain, world.m_seed, tile);
                    cachedSeed = world.m_seed;
                    cachedTile = tile;
                    Plugin.Log.LogInfo($"Britannia and Underworld terrain for seed '{world.m_seedName}' built in {sw.ElapsedMilliseconds} ms");
                }
                Pois = Core.Pois.Build(Plugin.U5);
                Settlements.Prepare(cached, cachedUnder, Pois);
                PlaceMarkers.Reset();
                Shrines.Reset();
                Seed = world.m_seed;
                Under = cachedUnder;
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
            Under = null;
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
