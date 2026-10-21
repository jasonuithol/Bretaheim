using System;
using System.Text;

namespace U5InValheim.Data
{
    /// <summary>A 32-byte character record of SAVED.GAM / INIT.GAM.</summary>
    public class CharacterRecord
    {
        public string Name;
        /// <summary>0x0b male, 0x0c female.</summary>
        public byte Gender;
        /// <summary>'A'vatar, 'B'ard, 'F'ighter, 'M'age.</summary>
        public char Class;
        /// <summary>'G'ood, 'P'oisoned, 'S'leeping, 'D'ead.</summary>
        public char Status;
        public int Str, Dex, Int, Mp, Hp, MaxHp, Exp, Level;
        /// <summary>0x19 = item worn by slot: helm, armour, weapon L, weapon R, ring, amulet (0xff none).</summary>
        public byte[] Equipment = new byte[6];
        /// <summary>Party slot / location byte (0 = in party).</summary>
        public byte PartyStatus;

        public static CharacterRecord Read(byte[] d, int o)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 9 && d[o + i] != 0; i++) sb.Append((char)d[o + i]);
            var c = new CharacterRecord
            {
                Name = sb.ToString(),
                Gender = d[o + 9],
                Class = (char)d[o + 0xa],
                Status = (char)d[o + 0xb],
                Str = d[o + 0xc],
                Dex = d[o + 0xd],
                Int = d[o + 0xe],
                Mp = d[o + 0xf],
                Hp = d[o + 0x10] | d[o + 0x11] << 8,
                MaxHp = d[o + 0x12] | d[o + 0x13] << 8,
                Exp = d[o + 0x14] | d[o + 0x15] << 8,
                Level = d[o + 0x16],
                PartyStatus = d[o + 0x1f],
            };
            Buffer.BlockCopy(d, o + 0x19, c.Equipment, 0, 6);
            return c;
        }
    }

    /// <summary>
    /// INIT.GAM / SAVED.GAM (4192 bytes; the image of DGROUP 0x55a6..0x6606). Only the fields the
    /// mod uses are decoded; offsets per reference/wiki_SAVED_GAM.txt.
    /// </summary>
    public class SaveGame
    {
        public const int Size = 0x1060;
        public const int ItemCount = 112;   // item ids 0..111 at 0x21a (equipment, spells, scrolls, potions)
        public const int ReagentCount = 8;

        readonly byte[] d;

        public SaveGame(byte[] data)
        {
            if (data.Length != Size) throw new U5FormatException($"save game has {data.Length} bytes, expected {Size}");
            d = data;
        }

        public CharacterRecord Character(int i) => CharacterRecord.Read(d, 2 + 0x20 * i);
        public int PartySize => d[0x2b5];
        public int Food => U16(0x202);
        public int Gold => U16(0x204);
        public int Keys => d[0x206];
        public int Gems => d[0x207];
        public int Torches => d[0x208];
        public bool Grapple => d[0x209] != 0;
        public int Carpets => d[0x20a];
        public int SkullKeys => d[0x20b];
        public bool AmuletOfLordBritish => d[0x20d] != 0;
        public bool CrownOfLordBritish => d[0x20e] != 0;
        public bool SceptreOfLordBritish => d[0x20f] != 0;
        public bool ShardOfFalsehood => d[0x210] != 0;
        public bool ShardOfHatred => d[0x211] != 0;
        public bool ShardOfCowardice => d[0x212] != 0;
        public int Spyglasses => d[0x214];
        public bool HmsCapePlans => d[0x215] != 0;
        public int Sextants => d[0x216];
        public bool PocketWatch => d[0x217] != 0;
        public bool BlackBadge => d[0x218] != 0;
        public bool SandalwoodBox => d[0x219] != 0;
        /// <summary>Count of item id 0..111 (0..47 equipment, 48..95 mixed spells, 96..103 scrolls, 104..111 potions).</summary>
        public int ItemQuantity(int itemId) => d[0x21a + itemId];
        public int Reagent(int reagent) => d[0x2aa + reagent];
        /// <summary>Moonstone 0..7: where it is buried (tile x, y; z 0 Britannia, −1 the Underworld), or carried.</summary>
        public (int x, int y, bool buried, int z) Moonstone(int i) => (d[0x28a + i], d[0x292 + i], d[0x29a + i] == 0, (sbyte)d[0x2a2 + i]);
        public int Year => U16(0x2ce);
        public int Month => d[0x2d7];
        public int Day => d[0x2d8];
        public int Hour => d[0x2d9];
        public int Minute => d[0x2db];
        /// <summary>The town (1 Moonglow .. 8 New Magincia; 0 none, 0xff destroyed) of Shadowlord 0..2 (Falsehood, Hatred, Cowardice).</summary>
        public int ShadowlordTown(int i) => d[0x322 + i];
        /// <summary>Shadowlord 0..2 (Falsehood, Hatred, Cowardice): the location it is in (0x58c8; 0xff destroyed).</summary>
        public int ShadowlordLocation(int i) => d[0x322 + i];
        public int Karma => d[0x2e2];
        public int Location => d[0x2ed];
        public int PartyZ => (sbyte)d[0x2ef];
        public int PartyX => d[0x2f0];
        public int PartyY => d[0x2f1];
        /// <summary>Dungeon 0..7 (Deceit … Doom) unsealed (g_dungeon_open 0x80); a sealed one's entrance shows as 0xdf until its word of power is spoken.</summary>
        public bool DungeonOpen(int i) => d[0x32a + i] >= 0x80;

        int U16(int o) => d[o] | d[o + 1] << 8;
    }
}
