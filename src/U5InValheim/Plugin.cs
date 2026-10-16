using System;
using System.Diagnostics;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Managers;
using Jotunn.Utils;
using U5InValheim.Data;

namespace U5InValheim
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGUID = "tarbaby.u5invalheim";
        public const string PluginName = "U5InValheim";
        public const string PluginVersion = "0.1.0";

        public static Plugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }

        /// <summary>The player's Ultima V data, or null when the configured directory is unusable.</summary>
        public static U5Data U5 { get; private set; }

        public static ConfigEntry<string> U5Directory { get; private set; }
        public static ConfigEntry<float> TileMetres { get; private set; }

        Harmony harmony;

        void Awake()
        {
            Instance = this;
            Log = Logger;

            U5Directory = Config.Bind("General", "UltimaVDirectory", "",
                "Folder of your own copy of Ultima V (e.g. the GOG install) containing DATA.OVL, BRIT.DAT, " +
                "TOWNE.DAT, ... The mod reads the world, towns, people and conversations from it at start-up; " +
                "no Ultima V data ships with the mod.");
            TileMetres = Config.Bind("World", "TileMetres", 32f,
                "Size of one Ultima V tile in metres (32 = Britannia is an 8.2 km square).");

            CommandManager.Instance.AddConsoleCommand(new U5Command());

            if (!LoadU5Data())
            {
                Log.LogError($"{PluginName} is disabled until UltimaVDirectory points at an Ultima V install " +
                             $"(edit {Config.ConfigFilePath} and restart).");
                return;
            }

            harmony = new Harmony(PluginGUID);
            harmony.PatchAll();
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

        public static string PluginDirectory => Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? "";
    }
}
