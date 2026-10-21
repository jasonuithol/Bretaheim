using System;
using System.Collections.Generic;

namespace U5InValheim.Data
{
    public enum LootKind
    {
        None, Chest, Gold, Potion, Scroll, Item, Keys, Gems, Torches, Food, SkullKeys,
        HmsCapePlans, SandalwoodBox, Moonstone, Carpet, Shard, Crown, Sceptre, AmuletOfLordBritish,
        Grapple, Sextant, Spyglass, BlackBadge,
    }

    /// <summary>Something the party can get: its kind, its value (item id, potion colour, scroll, chest quality …) and how many.</summary>
    public struct Loot
    {
        public LootKind Kind;
        public int Value, Count;

        public Loot(LootKind kind, int value = 0, int count = 1)
        {
            Kind = kind;
            Value = value;
            Count = count;
        }

        public override string ToString() => $"{Kind} {Value} ×{Count}";
    }

    /// <summary>An item hidden where only searching finds it (SJOG search, the DATA.OVL hidden-object table).</summary>
    public struct HiddenObject
    {
        public int Index, Tile, Value, Location, Z, X, Y;
        public Loot Loot => LootTables.Object(Tile, Value);
    }

    /// <summary>
    /// The original's loot (SJOG.OVL, COMBAT.OVL, TALK.OVL): what an object on the map gives when
    /// got (take_item), what a chest holds (roll_fixed_chest_loot, roll_random_chest_loot,
    /// spawn_chest_loot), whether a dying monster leaves a chest (apply_damage), the items hidden
    /// for searching, and conversation gifts.
    /// </summary>
    public static class LootTables
    {
        // DATA.OVL (DGROUP). The fixed roll's tables are read one past where the decompilation
        // names them (it counts 8..1): kinds chest, gold, potion, scroll, keys, gems, torches, food.
        const int FixedKind = 0x4124, FixedMin = 0x412c, FixedMax = 0x4134, RandomKind = 0x413c, RandomMin = 0x416c;
        const int HiddenTile = 0x3e78, HiddenValue = 0x3eea, HiddenLocation = 0x3f5c, HiddenZ = 0x3fce, HiddenX = 0x4040, HiddenY = 0x40b2;
        public const int HiddenCount = 113;
        const int Arrows = 27, Quarrels = 29;

        /// <summary>The original's rand_range(lo..hi), both ends included.</summary>
        static int Rand(Random rnd, int lo, int hi) => hi < lo ? lo : rnd.Next(lo, hi + 1);

        /// <summary>What an object (its tile less 0x100, and its value) gives when got; None for what cannot be got.</summary>
        public static Loot Object(int tile, int value)
        {
            switch (tile)
            {
                case 1: return new Loot(LootKind.Chest, value);
                case 2: return new Loot(LootKind.Gold, 0, value);
                case 3: return new Loot(LootKind.Potion, value);
                case 4: return value == 0xff ? new Loot(LootKind.HmsCapePlans) : new Loot(LootKind.Scroll, value);
                case 5: case 6: case 9: case 0xa: case 0xb: case 0xc:
                    return new Loot(LootKind.Item, value, value == Arrows || value == Quarrels ? 5 : 1);
                case 7: return value >= 0x80 ? new Loot(LootKind.SkullKeys, 0, value & 0x7f) : new Loot(LootKind.Keys, 0, value);
                case 8: return new Loot(LootKind.Gems, 0, value);
                case 0xd: return new Loot(LootKind.Torches, 0, value);
                case 0xe: return new Loot(LootKind.SandalwoodBox);
                case 0xf: return new Loot(LootKind.Food, 0, value);
                case 0x19: return new Loot(LootKind.Moonstone, value);
                case 0x1b: return new Loot(LootKind.Carpet);
                case 0xb4: return new Loot(LootKind.Shard, value & 3);
                case 0xb5: return new Loot(LootKind.Crown);
                case 0xb6: return new Loot(LootKind.Sceptre);
                case 0xb7: return new Loot(LootKind.AmuletOfLordBritish);
                default: return new Loot(LootKind.None);
            }
        }

        /// <summary>
        /// What a chest of this quality (0..127) holds when opened: the fixed roll (each of eight
        /// kinds when its minimum quality is met and beats a 1..30 roll) then quality/2 + 1 tries
        /// at the random table (an item by its id). A chest inside the chest is opened too.
        /// </summary>
        public static List<Loot> Chest(DataOvl ovl, int quality, Random rnd)
        {
            var r = new List<Loot>();
            Fill(ovl, quality & 0x7f, rnd, r, 0);
            return r;
        }

        static void Fill(DataOvl ovl, int q, Random rnd, List<Loot> into, int depth)
        {
            for (int i = 7; i >= 0; i--)
            {
                int min = ovl.U8(FixedMin + i);
                if (min > q || min > Rand(rnd, 1, 30)) continue;
                int max = ovl.I8(FixedMax + i);
                int amount = max == 1 ? 1 : Rand(rnd, 1, max);
                int kind = ovl.U8(FixedKind + i);
                // spawn_chest_loot: a chest's quality and gold are rolled again, potions and scrolls count from 0
                if (kind == 1) amount = Rand(rnd, 1, q);
                else if (kind == 2) amount = Rand(rnd, 1, 3 * q) & 0xff;
                else if (kind == 3 || kind == 4) amount--;
                var loot = Object(kind, amount);
                if (loot.Kind == LootKind.Chest) { if (depth < 4) Fill(ovl, loot.Value, rnd, into, depth + 1); }
                else if (loot.Kind != LootKind.None && loot.Count > 0) into.Add(loot);
            }
            for (int n = q / 2; n >= 0; n--)
            {
                int item = Rand(rnd, 0, 47);
                int min = ovl.U8(RandomMin + item);
                if (min > q || min > Rand(rnd, 1, 30)) continue;
                var loot = Object(ovl.U8(RandomKind + item), item);
                if (loot.Kind != LootKind.None) into.Add(loot);
            }
        }

        // DUNGEON chest (SJOG get_dungeon_chest_contents): seven rolls, each a minimum, a maximum and a kind
        const int DungeonMin = 0x41bc, DungeonMax = 0x41c4, DungeonKind = 0x41cc;

        /// <summary>
        /// What a chest standing in a dungeon cell gives (get_dungeon_chest_contents): seven rolls of
        /// 1..4·level+4, each taken when it reaches its minimum: food, gold (1..8·level), keys,
        /// gems, torches, a potion and a scroll (of a random colour/kind).
        /// </summary>
        public static List<Loot> DungeonChest(DataOvl ovl, int level, Random rnd)
        {
            var r = new List<Loot>();
            for (int i = 0; i < 7; i++)
            {
                int roll = Rand(rnd, 1, level * 4 + 4);
                if (ovl.U8(DungeonMin + i) > roll) continue;
                Loot loot;
                if (i == 5) loot = Object(3, Rand(rnd, 0, 7));
                else if (i == 6) loot = Object(4, Rand(rnd, 0, 7));
                else loot = Object(ovl.U8(DungeonKind + i), Rand(rnd, 1, i == 1 ? level << 3 : ovl.U8(DungeonMax + i)));
                if (loot.Kind != LootKind.None && loot.Count > 0) r.Add(loot);
            }
            return r;
        }

        /// <summary>A monster dying with this treasure value (its stats +6) leaves a chest of that quality, trapped or not, or a corpse (no chest).</summary>
        public static (bool chest, bool trapped) Drop(int treasure, Random rnd)
        {
            if (treasure <= 0 || treasure < Rand(rnd, 1, 30)) return (false, false);
            return (true, treasure > Rand(rnd, 1, 30));
        }

        public static List<HiddenObject> Hidden(DataOvl ovl)
        {
            var r = new List<HiddenObject>();
            for (int i = 0; i < HiddenCount; i++)
                r.Add(new HiddenObject
                {
                    Index = i, Tile = ovl.U8(HiddenTile + i), Value = ovl.U8(HiddenValue + i), Location = ovl.U8(HiddenLocation + i),
                    Z = ovl.I8(HiddenZ + i), X = ovl.U8(HiddenX + i), Y = ovl.U8(HiddenY + i),
                });
            return r;
        }

        /// <summary>The hidden objects found again: Blackthorn's keys (while the party has none), Minoc's skull keys (daily), the Glass Sword (while none is owned).</summary>
        public const int BlackthornKeys = 13, MinocSkullKeys = 14, GlassSword = 15;

        /// <summary>An object in a settlement's .NPC file that can be got (a slot whose type is one): the castle's chests (quality 30), the carpet, the box, the regalia.</summary>
        public static Loot SettlementObject(int type) => type == 1 ? new Loot(LootKind.Chest, 0x1e) : type == 0xe || type == 0x1b || (type >= 0xb4 && type <= 0xb7) ? Object(type, 0) : new Loot(LootKind.None);

        /// <summary>A conversation's gift (TALK opcode 0x86, its byte &amp; 0x7f).</summary>
        public static Loot Gift(int code)
        {
            if (code < 0x40) return new Loot(LootKind.Item, code);
            switch (code)
            {
                case 0x41: return new Loot(LootKind.Food);
                case 0x42: return new Loot(LootKind.Gold);
                case 0x43: return new Loot(LootKind.Keys);
                case 0x44: return new Loot(LootKind.Gems);
                case 0x45: return new Loot(LootKind.Torches);
                case 0x46: return new Loot(LootKind.Grapple);
                case 0x47: return new Loot(LootKind.Carpet);
                case 0x48: return new Loot(LootKind.Sextant);
                case 0x49: return new Loot(LootKind.Spyglass);
                case 0x4a: return new Loot(LootKind.BlackBadge);
                case 0x4b: return new Loot(LootKind.SkullKeys);
                default: return new Loot(LootKind.None);
            }
        }
    }
}
