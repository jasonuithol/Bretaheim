using System;

namespace Bretaheim.Data
{
    /// <summary>
    /// A creature type's stat line (DATA.OVL g_monster_stats 0x13bd[48][8], as COMBAT.OVL reads it:
    /// rust/src/ovl/combat.rs) and flags (g_monster_flags 0x153c). The same line serves monsters and
    /// the townsfolk who fight: a creature's damage and armour are its type's, whatever it holds.
    /// </summary>
    public class CreatureStats
    {
        public int Type;
        /// <summary>+0 dexterity (initiative, dodging), +1 attack skill (the hit roll), +2 armour, +3 damage per hit, +4 hit points, +5 group size, +6 treasure.</summary>
        public int Dexterity, Skill, Armour, Damage, HitPoints, GroupSize, Treasure;
        public ushort Flags;

        public bool Thief => (Flags & 0x2) != 0;
        public bool Poisonous => (Flags & 0x4) != 0;
        public bool Undead => (Flags & 0x20) != 0;

        public static CreatureStats Of(DataOvl ovl, int type)
        {
            var b = ovl.MonsterStats(type);
            return new CreatureStats
            {
                Type = type, Dexterity = b[0], Skill = b[1], Armour = b[2], Damage = b[3], HitPoints = b[4], GroupSize = b[5], Treasure = b[6],
                Flags = ovl.MonsterFlags(type),
            };
        }
    }

    /// <summary>
    /// Ultima's numbers in Valheim: one scale for every creature's hit points and every blow (the
    /// player's too, once they have Ultima stats), so a fight between any two of them is the
    /// original's fight, only on Valheim's health bars.
    /// </summary>
    public static class Ultima
    {
        public const float Scale = 10f;

        /// <summary>
        /// COMBAT.OVL roll_attack_damage: the attacker's damage less a roll of 1..the defender's
        /// armour (none below 0); the undead take half from anything but a spell (apply_damage).
        /// </summary>
        public static int Damage(int damage, int armour, bool undead, bool spell, Random rnd)
        {
            if (damage == 99) return 99;
            int d = armour > 0 ? damage - rnd.Next(1, armour + 1) : damage;
            if (undead && !spell) d /= 2;
            return Math.Max(0, d);
        }

        /// <summary>A party member's blow: a roll of 1..the weapon's attack value (a value of 1 is 1; the glass sword 99).</summary>
        public static int WeaponRoll(int attack, Random rnd) => attack > 1 && attack != 99 ? rnd.Next(1, attack + 1) : attack;
    }

    /// <summary>
    /// Random encounters on Britannia's surface (MAINOUT.OVL update_overworld_monsters,
    /// spawn_rate_for_terrain, choose_spawn_monster: rust/src/ovl/mainout.rs).
    /// </summary>
    public static class Encounters
    {
        // DATA.OVL: surface land / sea monster tiles and their weights (each weight table sums to 256)
        const int LandTiles = 0x2bc0, LandWeights = 0x2bdc, LandCount = 12, SeaTiles = 0x2bd4, SeaWeights = 0x2bf0, SeaCount = 6;
        public const int Whirlpool = 0xec, PirateShip = 0x2c, SandTrap = 0xe0;

        /// <summary>
        /// spawn_rate_for_terrain on the surface: 0 on roads and paths (0x20..0x26), 2 in swamp (4)
        /// and woods and hills (9..0xf), 1 elsewhere, plus 3 when the hour byte is above 0x1f or
        /// below 5 (the original's test: the first never holds, so only the small hours count).
        /// </summary>
        public static int Rate(byte tile, int hour, bool underworld = false)
        {
            // (the Underworld: always 3)
            if (underworld) return 3;
            int r = tile >= 0x20 && tile <= 0x26 ? 0 : tile == 4 || (tile > 8 && tile < 0x10) ? 2 : 1;
            if (hour > 0x1f || hour < 5) r += 3;
            return r;
        }

        /// <summary>A turn's encounter: a roll of 1..30 below the rate.</summary>
        public static bool Roll(byte tile, int hour, Random rnd, bool underworld = false) => rnd.Next(1, 31) < Rate(tile, hour, underworld);

        // DATA.OVL: the Underworld's land and water monster tiles and weights
        const int UnderLandTiles = 0x2bcc, UnderLandWeights = 0x2be8, UnderLandCount = 7, UnderWaterTiles = 0x2bda, UnderWaterWeights = 0x2bf6, UnderWaterCount = 2;
        /// <summary>What comes out of the Underworld's swamps.</summary>
        public const int UnderSwamp = 0xf8;

        /// <summary>
        /// choose_spawn_monster: the monster tile byte for a terrain tile, or 0 for none. Water:
        /// 1 in 4; in the Underworld its water table; on the surface a whirlpool 1 in 8 of those on
        /// deep water, else the sea table. Desert (7): a sand trap 1 in 4. Underworld swamp (4):
        /// 0xf8. Other land (below 0x10 but the peaks 0xc/0xd, or 0x30..0x33): the land table of
        /// the surface or of the Underworld.
        /// </summary>
        public static int Choose(DataOvl ovl, byte tile, Random rnd, bool underworld = false)
        {
            int t = tile;
            if (t < 4 || (t > 0x5f && t < 0x70) || (t > 0xd3 && t < 0xd8) || (t > 0xe3 && t < 0xe8))
            {
                if (rnd.Next(0, 65) >= 0x10) return 0;
                if (underworld) return Weighted(ovl, UnderWaterTiles, UnderWaterWeights, UnderWaterCount, rnd);
                if (t == 1 && rnd.Next(0, 8) == 7) return Whirlpool;
                return Weighted(ovl, SeaTiles, SeaWeights, SeaCount, rnd);
            }
            if (t == 7) return rnd.Next(0, 4) == 0 ? SandTrap : 0;
            if (t == 4 && underworld) return UnderSwamp;
            if (t != 0xc && t != 0xd && (t < 0x10 || (t & 0xfc) == 0x30))
                return underworld ? Weighted(ovl, UnderLandTiles, UnderLandWeights, UnderLandCount, rnd) : Weighted(ovl, LandTiles, LandWeights, LandCount, rnd);
            return 0;
        }

        /// <summary>weighted_random_index: a roll of 0..255 walked down the weights.</summary>
        static int Weighted(DataOvl ovl, int tiles, int weights, int count, Random rnd)
        {
            int r = rnd.Next(0, 256), i = 0;
            while (i < count - 1 && ovl.U8(weights + i) <= r) { r -= ovl.U8(weights + i); i++; }
            return ovl.U8(tiles + i);
        }

        /// <summary>The creature type of a monster tile byte (tile 0x100 + b; people start at 0x140, four frames each), or −1 for a ship or whirlpool.</summary>
        public static int TypeOf(int monsterTile) => monsterTile == PirateShip || monsterTile == Whirlpool || monsterTile < 0x40 ? -1 : (monsterTile + 0x100 - 0x140) / 4;

        /// <summary>How many come: 1 up to the type's group size.</summary>
        public static int Count(CreatureStats stats, Random rnd) => rnd.Next(1, Math.Max(1, stats.GroupSize) + 1);
    }
}
