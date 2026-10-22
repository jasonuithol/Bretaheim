using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Bretaheim.Data
{
    /// <summary>
    /// Plain-text dumps of every table, shared by the in-game "u5 dump" console command and
    /// tools/U5Dump. Each entry point returns the text; callers print it.
    /// </summary>
    public static class Dump
    {
        public const string Usage =
            "u5 dump <what> [args]\n" +
            "  locations                 the 40 places with map file, floors and overworld position\n" +
            "  map <location> [z]        a settlement floor as tile ids (hex)\n" +
            "  npcs <location>           NPCs: type, dialogue, schedule\n" +
            "  talk <location> <slot>    an NPC's conversation decoded\n" +
            "  items                     equipment names, attack/defence/range/price\n" +
            "  monsters                  creature names and raw stats\n" +
            "  spells | reagents | virtues | words\n" +
            "  dungeon <0-7|name> <level> [plan]  DUNGEON.DAT cells; plan: the level as built (88x88 tiles), its rooms\n" +
            "  room <index> | room <dungeon> <n>  a DUNGEON.CBT room (11x11 + metadata), by file index or by dungeon\n" +
            "  world <x> <y> [w] [h] [under]  overworld tile ids\n" +
            "  tiles [from] [count]      LOOK2.DAT tile names\n" +
            "  signs [location]          SIGNS.DAT\n" +
            "  init                      INIT.GAM: party, inventory, karma, date, position";

        public static string Run(U5Data d, string[] args)
        {
            if (args.Length == 0) return Usage;
            try
            {
                switch (args[0].ToLowerInvariant())
                {
                    case "locations": return Locations(d);
                    case "map": return Map(d, Need(args, 1), args.Length > 2 ? int.Parse(args[2]) : 0);
                    case "npcs": return Npcs(d, Need(args, 1));
                    case "talk": return Talk(d, Need(args, 1), int.Parse(Need(args, 2)));
                    case "items": return Items(d);
                    case "monsters": return Monsters(d);
                    case "colours": return Colours(d);
                    case "spells": return Lines(Enumerable.Range(0, 48).Select(i => $"{i,2} {d.Ovl.SpellName(i)}"));
                    case "reagents": return Lines(Enumerable.Range(0, 8).Select(i => $"{i} {d.Ovl.ReagentName(i)}"));
                    case "virtues":
                        var v = d.Ovl.Virtues; var m = d.Ovl.Mantras;
                        return Lines(Enumerable.Range(0, 8).Select(i => $"{i} {v[i],-12} {m[i]}"));
                    case "words":
                        var w = d.Ovl.WordsOfPower;
                        return Lines(Enumerable.Range(0, 8).Select(i => $"{i} {d.Location(33 + i).Name,-10} {w[i]}"));
                    case "dungeon":
                    {
                        int dn = DungeonNumber(d, Need(args, 1)), level = int.Parse(Need(args, 2));
                        return args.Length > 3 && args[3] == "plan" ? new WorldGen.Core.DungeonPlanner(d).Describe(dn, level) : Dungeon(d, dn, level);
                    }
                    case "room":
                        return args.Length > 2
                            ? Room(d.DungeonRooms[WorldGen.Core.DungeonInfo.RoomIndex(DungeonNumber(d, args[1]), int.Parse(args[2]))])
                            : Room(d.DungeonRooms[int.Parse(Need(args, 1))]);
                    case "world":
                        bool under = args.Contains("under");
                        var nums = args.Skip(1).Where(a => a != "under").Select(int.Parse).ToArray();
                        return World(under ? d.Underworld : d.Britannia, nums[0], nums[1],
                            nums.Length > 2 ? nums[2] : 32, nums.Length > 3 ? nums[3] : 16);
                    case "find":
                    {
                        // find <tile> [tile …] [under]: where those tiles are on the map
                        var map = args.Contains("under") ? d.Underworld : d.Britannia;
                        var want = new HashSet<int>(args.Skip(1).Where(a => a != "under").Select(a => a.StartsWith("0x") ? Convert.ToInt32(a, 16) : int.Parse(a)));
                        var hits = new List<string>();
                        for (int y = 0; y < WorldMap.Size; y++)
                        for (int x = 0; x < WorldMap.Size; x++)
                            if (want.Contains(map[x, y])) hits.Add($"{map[x, y],3} at ({x}, {y})");
                        return Lines(hits);
                    }
                    case "tiles":
                        int from = args.Length > 1 ? int.Parse(args[1]) : 0;
                        int count = args.Length > 2 ? int.Parse(args[2]) : 256;
                        return Lines(Enumerable.Range(from, Math.Min(count, TileNames.Count - from))
                            .Select(t => $"0x{t:x3} {d.TileNames[t]}"));
                    case "signs": return Signs(d, args.Length > 1 ? d.FindLocation(args[1]) : null);
                    case "init": return Init(d);
                    default: return $"unknown table '{args[0]}'\n{Usage}";
                }
            }
            catch (Exception e) when (e is FormatException || e is IndexOutOfRangeException || e is ArgumentException)
            {
                return $"error: {e.Message}\n{Usage}";
            }
        }

        /// <summary>A dungeon by number 0..7 or by name.</summary>
        static int DungeonNumber(U5Data d, string s)
        {
            if (int.TryParse(s, out int n)) return n;
            var l = d.FindLocation(s) ?? throw new ArgumentException($"no location '{s}'");
            if (l.IsSettlement) throw new ArgumentException($"{l.Name} is no dungeon");
            return l.Number - WorldGen.Core.DungeonInfo.FirstLocation;
        }

        static string Need(string[] a, int i) => i < a.Length ? a[i] : throw new ArgumentException("missing argument");
        static string Lines(System.Collections.Generic.IEnumerable<string> l) => string.Join("\n", l);

        static Location Loc(U5Data d, string s)
            => d.FindLocation(s) ?? throw new ArgumentException($"no location '{s}'");

        public static string Locations(U5Data d)
        {
            var sb = new StringBuilder("num name                    file      slot  x   y  floors (maps)\n");
            foreach (var l in d.Locations)
            {
                string floors = l.IsSettlement ? $"z {l.MinZ}..{l.MaxZ} ({l.MapIndex(l.MinZ)}..{l.MapIndex(l.MaxZ)})" : "dungeon";
                if (l.InUnderworld) floors += "  [underworld]";
                sb.AppendLine($"{l.Number,3} {l.Name,-23} {l.File,-9} {l.Slot,2} {l.X,3} {l.Y,3}  {floors}");
            }
            return sb.ToString();
        }

        public static string Map(U5Data d, string where, int z)
        {
            var l = Loc(d, where);
            if (!l.IsSettlement) return $"{l.Name} is a dungeon; use 'dungeon'";
            if (z < l.MinZ || z > l.MaxZ) return $"{l.Name} has floors {l.MinZ}..{l.MaxZ}";
            var m = d.Maps.Floor(l, z);
            var sb = new StringBuilder($"{l.Name} floor {z} ({l.File}.DAT map {l.MapIndex(z)})\n");
            for (int y = 0; y < SettlementMaps.Size; y++)
            {
                for (int x = 0; x < SettlementMaps.Size; x++) sb.Append(m[y * SettlementMaps.Size + x].ToString("x2")).Append(' ');
                sb.Length--;
                sb.Append('\n');
            }
            return sb.ToString();
        }

        public static string Npcs(U5Data d, string where)
        {
            var l = Loc(d, where);
            if (!l.IsSettlement) return $"{l.Name} has no NPCs";
            var sb = new StringBuilder($"{l.Name}: slot type dialog name | (ai x,y,z)x3 @ hours\n");
            foreach (var n in d.NpcsOf(l).Where(n => n.Slot > 0 && n.Exists))
            {
                var s = n.Schedule;
                string who = n.HasConversation ? d.NpcName(l, n)
                    : n.IsShopkeeper ? $"[{n.Trade}]" : n.IsGuard ? "[guard]" : n.Dialog == 0 ? "[no response]" : $"[0x{n.Dialog:x2}]";
                string places = string.Join(" ", Enumerable.Range(0, 3).Select(k => $"({s.Ai[k]} {s.X[k]},{s.Y[k]},{s.Z[k]})"));
                sb.AppendLine($"{n.Slot,2} 0x{n.Type:x2} 0x{n.Dialog:x2} {who,-20} {places} @ {string.Join(",", s.Times)}");
            }
            return sb.ToString();
        }

        public static string Talk(U5Data d, string where, int slot)
        {
            var l = Loc(d, where);
            var n = d.NpcsOf(l)[slot];
            var s = d.ScriptOf(l, n);
            if (s == null) return $"{l.Name} slot {slot} has no conversation (dialog 0x{n.Dialog:x2})";
            string R(byte[] b) => b == null ? "" : d.RenderTlk(b);
            var sb = new StringBuilder();
            sb.AppendLine($"{l.Name} slot {slot}, {l.File}.TLK entry {s.Npc}");
            sb.AppendLine($"NAME  {R(s.Name)}");
            sb.AppendLine($"LOOK  {R(s.Description)}");
            sb.AppendLine($"HELLO {R(s.Greeting)}");
            sb.AppendLine($"JOB   {R(s.Job)}");
            sb.AppendLine($"BYE   {R(s.Bye)}");
            foreach (var k in s.Keywords) sb.AppendLine($"[{string.Join("|", k.Keys)}] {R(k.Answer)}");
            foreach (var lb in s.Labels)
            {
                sb.AppendLine($"LABEL {lb.Number}: {R(lb.Question)}");
                if (lb.DefaultAnswer != null) sb.AppendLine($"   default: {R(lb.DefaultAnswer)}");
                foreach (var k in lb.Keywords) sb.AppendLine($"   [{string.Join("|", k.Keys)}] {R(k.Answer)}");
            }
            return sb.ToString();
        }

        public static string Items(U5Data d)
        {
            var sb = new StringBuilder("id name                 atk def rng price\n");
            for (int i = 0; i < 48; i++)
                sb.AppendLine($"{i,2} {d.Ovl.ItemName(i),-20} {d.Ovl.ItemAttack(i),3} {d.Ovl.ItemDefense(i),3} {d.Ovl.ItemRange(i),3} {d.Ovl.ItemPrice(i),5}");
            return sb.ToString();
        }

        public static string Monsters(U5Data d)
        {
            var sb = new StringBuilder("type name             plural            stats(8 raw bytes)        flags\n");
            for (int i = 0; i < 48; i++)
                sb.AppendLine($"{i,2} {d.Ovl.CreatureName(i),-16} {d.Ovl.CreatureNamePlural(i),-17} {BitConverter.ToString(d.Ovl.MonsterStats(i))} 0x{d.Ovl.MonsterFlags(i):x4}");
            return sb.ToString();
        }

        /// <summary>Each creature's pixels per EGA colour (0 black .. 15 white) over its four frames in TILES.16, and the colour it is shaded.</summary>
        public static string Colours(U5Data d)
        {
            var sb = new StringBuilder("type name             " + string.Join(" ", Enumerable.Range(0, 16).Select(c => $"{c,4}")) + "  colour\n");
            for (int i = 16; i < 48; i++)
            {
                var c = CreatureColours.Dominant(d.Tiles, i);
                sb.AppendLine($"{i,2} {d.Ovl.CreatureName(i),-16} " + string.Join(" ", CreatureColours.Histogram(d.Tiles, i).Select(n => $"{n,4}")) + (c.HasValue ? $"  #{c.Value:x6}" : "  -"));
            }
            return sb.ToString();
        }

        public static string Dungeon(U5Data d, int dungeon, int level)
        {
            var sb = new StringBuilder($"{d.Location(33 + dungeon).Name} level {level}\n");
            for (int y = 0; y < DungeonMaps.Size; y++)
            {
                for (int x = 0; x < DungeonMaps.Size; x++) sb.Append(d.Dungeons.Cell(dungeon, level, x, y).Raw.ToString("x2")).Append(' ');
                sb.Length--;
                sb.Append('\n');
            }
            return sb.ToString();
        }

        public static string Room(CombatMap r)
        {
            var sb = new StringBuilder();
            for (int y = 0; y < CombatMap.Size; y++)
            {
                for (int x = 0; x < CombatMap.Size; x++) sb.Append(r.Tiles[y, x].ToString("x2")).Append(' ');
                sb.Length--;
                sb.Append('\n');
            }
            sb.AppendLine($"monsters: {Hex(r.MonsterTiles)}\n  x: {Hex(r.MonsterX)}\n  y: {Hex(r.MonsterY)}");
            sb.AppendLine($"triggers x: {Hex(r.TriggerX)} y: {Hex(r.TriggerY)}  new tiles: {Hex(r.NewTileIds)}");
            return sb.ToString();
        }

        public static string World(WorldMap w, int x0, int y0, int width, int height)
        {
            var sb = new StringBuilder();
            for (int y = y0; y < y0 + height; y++)
            {
                for (int x = x0; x < x0 + width; x++) sb.Append(w[x, y].ToString("x2")).Append(' ');
                sb.Length--;
                sb.Append('\n');
            }
            return sb.ToString();
        }

        public static string Signs(U5Data d, Location only)
        {
            var sb = new StringBuilder();
            for (int g = 0; g < d.Signs.Length; g++)
                foreach (var s in d.Signs[g])
                    if (only == null || s.Location == only.Number)
                        sb.AppendLine($"group {g,2} loc {s.Location,2} z {s.Z} ({s.X},{s.Y}): {s.Text}");
            return sb.ToString();
        }

        public static string Init(U5Data d)
        {
            var g = d.InitialGame;
            var sb = new StringBuilder();
            for (int i = 0; i < 16; i++)
            {
                var c = g.Character(i);
                if (c.Name.Length == 0) continue;
                sb.AppendLine($"{i,2} {c.Name,-9} {c.Class} {c.Status} STR {c.Str,2} DEX {c.Dex,2} INT {c.Int,2} MP {c.Mp,2} HP {c.Hp}/{c.MaxHp} XP {c.Exp} L{c.Level} gear {Hex(c.Equipment)} party 0x{c.PartyStatus:x2}");
            }
            sb.AppendLine($"party {g.PartySize}  food {g.Food}  gold {g.Gold}  keys {g.Keys}  gems {g.Gems}  torches {g.Torches}  karma {g.Karma}");
            sb.AppendLine($"date {g.Day}-{g.Month}-{g.Year} {g.Hour:00}:{g.Minute:00}  location {g.Location} ({g.PartyX},{g.PartyY},{g.PartyZ})");
            sb.Append("items:");
            for (int i = 0; i < SaveGame.ItemCount; i++)
                if (g.ItemQuantity(i) != 0) sb.Append($" {d.Ovl.InventoryName(i)}x{g.ItemQuantity(i)}");
            sb.Append("\nreagents:");
            for (int i = 0; i < SaveGame.ReagentCount; i++) sb.Append($" {d.Ovl.ReagentName(i)}x{g.Reagent(i)}");
            return sb.AppendLine().ToString();
        }

        static string Hex(byte[] b) => string.Join(" ", b.Select(x => x.ToString("x2")));
    }
}
