using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Bretaheim.Data
{
    /// <summary>
    /// Everything read from the player's own Ultima V directory, parsed once into immutable
    /// tables. Nothing here touches Unity, so the same code runs in the plugin, the offline dump
    /// tool and the tests.
    /// </summary>
    public class U5Data
    {
        /// <summary>Files that must exist for <see cref="Validate"/> to pass.</summary>
        public static readonly string[] RequiredFiles =
        {
            "DATA.OVL", "BRIT.DAT", "UNDER.DAT", "TOWNE.DAT", "DWELLING.DAT", "CASTLE.DAT", "KEEP.DAT",
            "TOWNE.NPC", "DWELLING.NPC", "CASTLE.NPC", "KEEP.NPC", "TOWNE.TLK", "DWELLING.TLK", "CASTLE.TLK",
            "KEEP.TLK", "DUNGEON.DAT", "DUNGEON.CBT", "BRIT.CBT", "LOOK2.DAT", "SIGNS.DAT", "INIT.GAM", "TILES.16", "SHOPPE.DAT",
        };

        public string Directory { get; private set; }
        public DataOvl Ovl { get; private set; }
        public WorldMap Britannia { get; private set; }
        public WorldMap Underworld { get; private set; }
        public SettlementMaps Maps { get; private set; }
        public Location[] Locations { get; private set; }
        public NpcFile[] Npcs { get; private set; }   // by SettlementFile 0..3
        public TlkFile[] Talk { get; private set; }   // by SettlementFile 0..3
        public DungeonMaps Dungeons { get; private set; }
        public List<CombatMap> DungeonRooms { get; private set; }
        public List<CombatMap> BritCombatMaps { get; private set; }
        public TileNames TileNames { get; private set; }
        public List<Sign>[] Signs { get; private set; }
        public SaveGame InitialGame { get; private set; }
        public TileSet Tiles { get; private set; }
        /// <summary>The shops (SHOPPE.DAT texts, DATA.OVL stock and prices).</summary>
        public Shops Shops { get; private set; }

        /// <summary>Returns the missing required files (empty when the directory is usable).</summary>
        public static List<string> Validate(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir)) return RequiredFiles.ToList();
            return RequiredFiles.Where(f => FindFile(dir, f) == null).ToList();
        }

        public static U5Data Load(string dir)
        {
            var missing = Validate(dir);
            if (missing.Count > 0)
                throw new FileNotFoundException($"Ultima V directory '{dir}' is missing: {string.Join(", ", missing)}");

            byte[] Read(string name) => File.ReadAllBytes(FindFile(dir, name));

            var d = new U5Data { Directory = dir };
            d.Ovl = new DataOvl(Read("DATA.OVL"));
            d.Britannia = WorldMap.FromBrit(Read("BRIT.DAT"), d.Ovl.BritChunkMap);
            d.Underworld = WorldMap.FromUnder(Read("UNDER.DAT"));
            d.Maps = new SettlementMaps(Read);
            d.Locations = LocationTable.Build(d.Ovl, d.Maps, d.Britannia, d.Underworld);
            d.Npcs = new[] { "TOWNE", "DWELLING", "CASTLE", "KEEP" }.Select(n => new NpcFile(Read(n + ".NPC"))).ToArray();
            d.Talk = new[] { "TOWNE", "DWELLING", "CASTLE", "KEEP" }.Select(n => new TlkFile(Read(n + ".TLK"))).ToArray();
            d.Dungeons = new DungeonMaps(Read("DUNGEON.DAT"));
            d.DungeonRooms = CombatMap.ReadAll(Read("DUNGEON.CBT"));
            d.BritCombatMaps = CombatMap.ReadAll(Read("BRIT.CBT"));
            d.TileNames = new TileNames(Read("LOOK2.DAT"));
            d.Signs = SignsFile.Read(Read("SIGNS.DAT"));
            d.InitialGame = new SaveGame(Read("INIT.GAM"));
            d.Tiles = new TileSet(Read("TILES.16"));
            d.Shops = new Shops(d, Read("SHOPPE.DAT"));
            return d;
        }

        /// <summary>Case-insensitive lookup (GOG ships upper case; other copies may not).</summary>
        public static string FindFile(string dir, string name)
        {
            string exact = Path.Combine(dir, name);
            if (File.Exists(exact)) return exact;
            foreach (var f in System.IO.Directory.EnumerateFiles(dir))
                if (string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase)) return f;
            return null;
        }

        public Location Location(int number) => Locations[number - 1];

        public Location FindLocation(string nameOrNumber)
        {
            if (int.TryParse(nameOrNumber, out int n) && n >= 1 && n <= LocationTable.Count) return Location(n);
            return Locations.FirstOrDefault(l => string.Equals(l.Name, nameOrNumber, StringComparison.OrdinalIgnoreCase))
                ?? Locations.FirstOrDefault(l => l.Name.StartsWith(nameOrNumber, StringComparison.OrdinalIgnoreCase));
        }

        public SettlementNpc[] NpcsOf(Location loc) => Npcs[(int)loc.File].Settlement(loc.Slot);
        public TlkScript ScriptOf(Location loc, SettlementNpc npc)
            => npc.HasConversation ? Talk[(int)loc.File][npc.Dialog] : null;

        /// <summary>The NPC's name: its name section up to the first quote or line break (some
        /// name sections carry script after the name, e.g. Eb in Britain).</summary>
        public string NpcName(Location loc, SettlementNpc npc)
        {
            var s = ScriptOf(loc, npc);
            if (s == null) return null;
            string name = RenderTlk(s.Name, markers: false);
            int cut = name.IndexOfAny(new[] { '"', '\n' });
            return (cut >= 0 ? name.Substring(0, cut) : name).Trim();
        }

        public string RenderTlk(byte[] text, string avatar = null, bool markers = true)
            => TlkText.Render(text, Ovl.TlkWord, avatar, markers);
    }
}
