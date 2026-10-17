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
        public static ConfigEntry<string> BuildSettlements { get; private set; }
        public static ConfigEntry<string> CombinedShells { get; private set; }

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

            BuildSettlements = Config.Bind("World", "BuildSettlements", "2,13,17",
                "Location numbers of the settlements built from their maps in new Britannia worlds " +
                "(2 Britain, 13 Iolo's hut, 17 Lord British's castle; see 'u5 dump locations'). " +
                "Must be the same on the server and every client.");
            CombinedShells = Config.Bind("World", "CombinedShells", "*",
                "Settlements whose building shells (walls, floors, roofs) are built as one combined object per " +
                "zone instead of thousands of separate pieces (* = all of them). Must match on every peer.");
            DumpPieceGeometry = Config.Bind("Development", "DumpPieceGeometry", "",
                "Comma-separated prefab names (or *) whose snap points and bounds are written to " +
                "BepInEx/u5_piece_geometry.json at start-up. Leave empty.");

            SelfTest = Config.Bind("Development", "SelfTest", false, "Log development self-checks at start-up. Leave off.");

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
                FixedPieces.Install(harmony);
                gameObject.AddComponent<WorldWrap>();
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
