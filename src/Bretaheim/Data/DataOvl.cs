using System;
using System.Collections.Generic;
using System.Text;

namespace Bretaheim.Data
{
    /// <summary>
    /// DATA.OVL, addressed by DGROUP offset (file offset − 0x10) so that every table offset can be
    /// taken verbatim from the decompilation (<see cref="Dg"/>, analysis/globals_seed.jsonl).
    /// String pointers stored in the file are DGROUP offsets too.
    /// </summary>
    public class DataOvl
    {
        public const int FileToDgroup = 0x10;

        readonly byte[] file;

        public DataOvl(byte[] file)
        {
            if (file.Length < 0x6000) throw new U5FormatException($"DATA.OVL too short ({file.Length} bytes)");
            this.file = file;
        }

        public int Length => file.Length - FileToDgroup;

        public byte U8(int dg) => file[dg + FileToDgroup];
        public sbyte I8(int dg) => (sbyte)file[dg + FileToDgroup];
        public ushort U16(int dg) => (ushort)(file[dg + FileToDgroup] | file[dg + FileToDgroup + 1] << 8);

        public byte[] Bytes(int dg, int count)
        {
            var r = new byte[count];
            Buffer.BlockCopy(file, dg + FileToDgroup, r, 0, count);
            return r;
        }

        public ushort[] Words(int dg, int count)
        {
            var r = new ushort[count];
            for (int i = 0; i < count; i++) r[i] = U16(dg + 2 * i);
            return r;
        }

        /// <summary>NUL-terminated string at a DGROUP offset; the high bit of each char is dropped.</summary>
        public string CStr(int dg)
        {
            var sb = new StringBuilder();
            for (int p = dg + FileToDgroup; p < file.Length && file[p] != 0; p++) sb.Append((char)(file[p] & 0x7f));
            return sb.ToString();
        }

        /// <summary>String through a near-pointer table: table[index] → DGROUP string; NULL → null.</summary>
        public string PtrStr(int tableDg, int index)
        {
            int p = U16(tableDg + 2 * index);
            return p == 0 ? null : CStr(p);
        }

        public string[] PtrStrs(int tableDg, int count)
        {
            var r = new string[count];
            for (int i = 0; i < count; i++) r[i] = PtrStr(tableDg, i);
            return r;
        }

        /// <summary>Consecutive NUL-separated strings filling <paramref name="size"/> bytes.</summary>
        public List<string> CStrTable(int dg, int size)
        {
            var r = new List<string>();
            var sb = new StringBuilder();
            for (int p = dg; p < dg + size; p++)
            {
                byte b = U8(p);
                if (b == 0)
                {
                    if (sb.Length > 0) r.Add(sb.ToString());
                    sb.Clear();
                }
                else sb.Append((char)(b & 0x7f));
            }
            if (sb.Length > 0) r.Add(sb.ToString());
            return r;
        }

        // ---- named tables -------------------------------------------------------------------

        /// <summary>Equipment name by item id 0..47 (g_name_ptrs[0..47]).</summary>
        public string ItemName(int itemId) => PtrStr(Dg.NamePtrs, itemId);
        /// <summary>Singular creature name by creature type 0..47.</summary>
        public string CreatureName(int type) => PtrStr(Dg.CreatureNamePtrs, type);
        /// <summary>Plural capitalised creature name by creature type 0..47.</summary>
        public string CreatureNamePlural(int type) => PtrStr(Dg.MonsterNamePtrs, type);
        public string SpellName(int spell) => PtrStr(Dg.SpellNamePtrs, spell);
        public string ReagentName(int reagent) => PtrStr(Dg.ReagentNamePtrs, reagent);
        public string PotionName(int potion) => PtrStr(Dg.PotionNamePtrs, potion);
        public string ClassName(int cls) => PtrStr(Dg.ClassNamePtrs, cls);
        public string ShortItemName(int itemId) => PtrStr(Dg.ShortNamePtrs, itemId);

        /// <summary>
        /// Name of any inventory item id 0..111: 0..47 equipment, 48..95 mixed spells (spell id − 48),
        /// 96..103 scrolls (their abbreviation), 104..111 potions (colour).
        /// </summary>
        public string InventoryName(int itemId)
        {
            if (itemId < 48) return ItemName(itemId);
            if (itemId < 96) return SpellName(itemId - 48);
            if (itemId < 104) return "Scroll " + PtrStr(Dg.ScrollNamePtrs, itemId - 96);
            if (itemId < 112) return PotionName(itemId - 104) + " Potion";
            return null;
        }

        /// <summary>
        /// A spell's words in full ("In Vas Por Ylem"): its name in DATA.OVL is cut at 10 letters,
        /// and every word of power begins with its own letter (the rune syllables, one per letter).
        /// </summary>
        public string SpellWords(int spell)
        {
            var words = SpellName(spell).Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                if (words[i].Length == 0) continue;
                int letter = char.ToUpperInvariant(words[i][0]) - 'A';
                string syllable = letter >= 0 && letter < 26 ? PtrStr(Dg.RuneSyllablePtrs, letter) : null;
                if (syllable != null) words[i] = syllable.Substring(0, 1) + syllable.Substring(1).ToLowerInvariant();
            }
            return string.Join(" ", words);
        }

        public string[] Virtues => PtrStrs(Dg.VirtueMantraPtrs, 8);
        public string[] Mantras
        {
            get
            {
                var r = new string[8];
                for (int i = 0; i < 8; i++) r[i] = PtrStr(Dg.VirtueMantraPtrs, 8 + i);
                return r;
            }
        }
        public string[] WordsOfPower => PtrStrs(Dg.WordOfPowerPtrs, 8);

        public byte ItemAttack(int itemId) => U8(Dg.ItemAttack + itemId);
        public byte ItemDefense(int itemId) => itemId < 0x2f ? U8(Dg.ItemDefense + itemId) : (byte)0;
        public byte ItemRange(int itemId) => U8(Dg.ItemRange + itemId);
        public ushort ItemPrice(int itemId) => U16(Dg.ItemPrices + 2 * itemId);

        /// <summary>Location numbers with a shop of this trade, in the order of its tables (g_shop_towns).</summary>
        public List<int> ShopTowns(ShopTrade trade)
        {
            var r = new List<int>();
            for (int i = 0; i < 16; i++)
            {
                byte loc = U8(Dg.ShopTowns + 16 * (int)trade + i);
                if (loc == 0) break;
                r.Add(loc);
            }
            return r;
        }

        /// <summary>Equipment item ids (1..47) sold by the armourer of a location, or none (rows end in 0 or 0xff padding).</summary>
        public List<int> ArmouryStock(int location)
        {
            var r = new List<int>();
            int shop = ShopTowns(ShopTrade.Armoury).IndexOf(location);
            if (shop < 0) return r;
            foreach (byte id in Bytes(Dg.ArmouryStock + 8 * shop, 8))
                if (id > 0 && id < 48) r.Add(id);
            return r;
        }

        /// <summary>The 8 raw stat bytes of creature type 0..47 (g_monster_stats).</summary>
        public byte[] MonsterStats(int type) => Bytes(Dg.MonsterStats + 8 * type, 8);
        public ushort MonsterFlags(int type) => U16(Dg.MonsterFlags + 2 * type);

        /// <summary>Compressed .TLK word for code 0..128 (null for unused codes).</summary>
        public string TlkWord(int code) => PtrStr(Dg.TlkWordPtrs, code);

        public byte[] BritChunkMap => Bytes(Dg.BritChunkMap, 256);

        /// <summary>Trammel/Felucca phase chars ('0'..'7') for day 1..28.</summary>
        public (int trammel, int felucca) MoonPhase(int day)
            => (U8(Dg.MoonPhases + 2 * (day - 1)) - '0', U8(Dg.MoonPhases + 2 * (day - 1) + 1) - '0');

        /// <summary>Is tile 0..255 walkable for NPCs under walk bitmap <paramref name="map"/> 0..7 (MSB first).</summary>
        public bool NpcCanWalk(int map, int tile)
            => (U8(Dg.NpcWalkBitmaps + 32 * map + (tile >> 3)) & (0x80 >> (tile & 7))) != 0;
    }
}
