using System;
using System.Diagnostics;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Utils;
using U5InValheim.Data;
using U5InValheim.WorldGen;

namespace U5InValheim
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGUID = "tarbaby.u5invalheim";
        public const string PluginName = "U5InValheim";
        public const string PluginVersion = "0.2.0";

        public static Plugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }

        /// <summary>The player's Ultima V data, or null when the configured directory is unusable.</summary>
        public static U5Data U5 { get; private set; }

        public static ConfigEntry<string> U5Directory { get; private set; }
        public static ConfigEntry<float> TileMetres { get; private set; }
        public static ConfigEntry<bool> NewWorldsAreBritannia { get; private set; }
        public static ConfigEntry<string> DumpPieceGeometry { get; private set; }
        public static ConfigEntry<bool> SelfTest { get; private set; }
        public static ConfigEntry<bool> LogHitches { get; private set; }
        public static ConfigEntry<string> BuildSettlements { get; private set; }
        public static ConfigEntry<string> CombinedShells { get; private set; }
        public static ConfigEntry<int> SpawnLocation { get; private set; }

        Harmony harmony;

        void Awake()
        {
            Instance = this;
            Log = Logger;
            PluginDirectory = Path.GetDirectoryName(Info.Location) ?? "";

            U5Directory = Config.Bind("General", "UltimaVDirectory", "",
                "Folder of your own copy of Ultima V (e.g. the GOG install) containing DATA.OVL, BRIT.DAT, " +
                "TOWNE.DAT, ... The mod reads the world, towns, people and conversations from it at start-up; " +
                "no Ultima V data ships with the mod.");
            TileMetres = Config.Bind("World", "TileMetres", 32f,
                new ConfigDescription("Size of one Ultima V tile in metres (32 = Britannia is an 8.2 km square). " +
                    "Must be the same on the server and every client.", new AcceptableValueRange<float>(8f, 36f)));
            NewWorldsAreBritannia = Config.Bind("World", "NewWorldsAreBritannia", true,
                "New worlds get a seed starting with \"Britannia\", which makes them Britannia. A world is Britannia " +
                "exactly when its seed starts with \"Britannia\"; worlds with other seeds stay vanilla.");

            BuildSettlements = Config.Bind("World", "BuildSettlements", "1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,26,27,28,29,30,31,32",
                "Location numbers of the settlements built from their maps in new Britannia worlds " +
                "(2 Britain, 4 Yew, 13 Iolo's hut, 17 Lord British's castle, 19-21 West, North and East Britanny; see 'u5 dump locations'). " +
                "Must be the same on the server and every client.");
            CombinedShells = Config.Bind("World", "CombinedShells", "*",
                "Settlements whose building shells (walls, floors, roofs) are built as one combined object per " +
                "zone instead of thousands of separate pieces (* = all of them). Must match on every peer.");
            SpawnLocation = Config.Bind("World", "SpawnLocation", 13,
                "Where players first appear in a new Britannia world: 13 = inside Iolo's hut (the game's start), " +
                "or the location number of a built settlement to start just outside it (2 = Britain). " +
                "Read by whoever creates the world (the server, or the host); existing worlds keep their spawn.");
            DumpPieceGeometry = Config.Bind("Development", "DumpPieceGeometry", "",
                "Comma-separated prefab names (or *) whose snap points and bounds are written to " +
                "BepInEx/u5_piece_geometry.json at start-up. Leave empty.");

            SelfTest = Config.Bind("Development", "SelfTest", false, "Log development self-checks at start-up. Leave off.");
            LogHitches = Config.Bind("Development", "LogHitches", true, "Log every frame over 150 ms (\"hitch\") with what ran in it: zone spawning, terrain, object creation, settlement pieces, townsfolk, structures.");

            harmony = new Harmony(PluginGUID);
            DevTools.PieceGeometryDump.Install(harmony);
            U5Command.Install(harmony); // also explains a missing UltimaVDirectory in-game

            if (!LoadU5Data())
            {
                Log.LogError($"{PluginName} is disabled until UltimaVDirectory points at an Ultima V install " +
                             $"(edit {Config.ConfigFilePath} and restart).");
                return;
            }

            harmony.PatchAll();
            WorldGenPatches.Install(harmony);
            if (WorldGenPatches.Installed)
            {
                PlaceMarkers.Install(harmony);
                Settlements.Install(harmony);
                Structures.Register();
                TownsfolkSpawner.Register();
                Ballistas.Register();
                Ettin.Register();
                Camp.Register();
                Ladders.Register();
                UltimaItems.Register();
                SpellStatus.Register();
                Treasure.Register();
                TalkPanel.Install(harmony);
                ShopTrading.Install(harmony);
                StartingGold.Install(harmony);
                TownWatch.Install(harmony);
                TownsfolkPatches.Install(harmony);
                UltimaCreatures.Install(harmony);
                AvatarStats.Install(harmony);
                Ballistas.Install(harmony);
                Crime.Install(harmony);
                TownDoors.Install(harmony);
                CreatureTalker.Install(harmony);
                ResidentMonster.Install(harmony);
                Sleep.Install(harmony);
                Respawn.Install(harmony);
                TerrainQueue.Install(harmony);
                Treasure.Install(harmony);
                Spells.Install(harmony);
                if (LogHitches.Value) DevTools.LoadProfiler.Install(harmony);
                gameObject.AddComponent<Crime.Watch>();
                gameObject.AddComponent<Camp.Keys>();
                gameObject.AddComponent<Spells.Keys>();
                gameObject.AddComponent<UltimaSpawner>();
                FixedPieces.Install(harmony);
                BiomeBlend.Install(harmony);
                RiverBanks.Install(harmony);
                gameObject.AddComponent<WorldWrap>();
                if (SelfTest.Value) gameObject.AddComponent<DevTools.TownsfolkSelfTest>();
                if (LogHitches.Value) gameObject.AddComponent<DevTools.LoadProfiler>();
            }
            Log.LogInfo($"{PluginName} v{PluginVersion} loaded.");
        }

        void OnDestroy() => harmony?.UnpatchSelf();

        static bool LoadU5Data()
        {
            string dir = U5Directory.Value;
            if (string.IsNullOrWhiteSpace(dir))
            {
                Log.LogError("UltimaVDirectory is not set.");
                return false;
            }
            dir = Environment.ExpandEnvironmentVariables(dir.Trim());
            if (dir.StartsWith("~")) dir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + dir.Substring(1);

            var missing = U5Data.Validate(dir);
            if (missing.Count > 0)
            {
                Log.LogError($"UltimaVDirectory '{dir}' is not a usable Ultima V install; missing: {string.Join(", ", missing)}");
                return false;
            }
            try
            {
                var sw = Stopwatch.StartNew();
                U5 = U5Data.Load(dir);
                Log.LogInfo($"Read Ultima V from '{dir}' in {sw.ElapsedMilliseconds} ms: " +
                            $"{U5.Locations.Length} locations, {U5.DungeonRooms.Count} dungeon rooms.");
                return true;
            }
            catch (Exception e) when (e is IOException || e is U5FormatException || e is IndexOutOfRangeException)
            {
                Log.LogError($"Could not read Ultima V from '{dir}': {e.Message}");
                return false;
            }
        }

        /// <summary>Folder of the plugin DLL, read once at start-up.</summary>
        public static string PluginDirectory { get; private set; } = "";
    }
}
