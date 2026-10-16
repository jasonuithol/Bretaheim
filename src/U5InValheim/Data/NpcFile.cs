using System;

namespace U5InValheim.Data
{
    /// <summary>
    /// One NPC's daily plan from a .NPC file: three places (AI type, x, y, z) and four hours.
    /// From times[0] the NPC goes to place 0, from times[1] place 1, from times[2] place 2 and from
    /// times[3] back to place 1 (NPC.OVL schedule_index_for_hour).
    /// </summary>
    public class NpcSchedule
    {
        public byte[] Ai = new byte[3];
        public byte[] X = new byte[3];
        public byte[] Y = new byte[3];
        public sbyte[] Z = new sbyte[3];
        public byte[] Times = new byte[4];

        /// <summary>
        /// Place index 0..2 the NPC is heading to at <paramref name="hour"/>. Exact port of NPC.OVL
        /// schedule_index_for_hour (1000:b570): the entry with the smallest (hour − entry hour) as an
        /// unsigned byte, ties to the earlier entry; the 4th hour maps back to place 1.
        /// </summary>
        public int PlaceAt(int hour)
        {
            byte h = (byte)hour;
            byte d0 = (byte)(h - Times[0]), d1 = (byte)(h - Times[1]);
            byte d2 = (byte)(h - Times[2]), d3 = (byte)(h - Times[3]);
            byte best = d0;
            int r = d1 < d0 ? 1 : 0;
            if (d1 < best) best = d1;
            if (d2 < best) { r = 2; best = d2; }
            if (d3 < best) r = 1;
            return r;
        }
    }

    public class SettlementNpc
    {
        /// <summary>Slot 1..31 within the settlement (slot 0 is unused).</summary>
        public int Slot;
        public NpcSchedule Schedule;
        /// <summary>Type byte = tile index − 0x100 of the NPC's sprite (e.g. 0x50 guard).</summary>
        public byte Type;
        /// <summary>0 no response; 1..0x7f .TLK entry; 0x81..0x88 shopkeeper (trade = value − 0x81); 0xff guard.</summary>
        public byte Dialog;

        public bool Exists => Type != 0 || Dialog != 0;
        /// <summary>Sprite tile (0x100 + type): 0x140.. are people in groups of 4 frames, lower ones animals/objects (horses 0x110).</summary>
        public int Tile => 0x100 + Type;
        /// <summary>Creature type 0..15 (Mage, Bard, Fighter, Avatar, Villager, Merchant, Jester, Bard, Pirate, -, Child, Beggar, Guard, Wanderer, Blackthorn, Lord British) for people, else −1.</summary>
        public int CreatureType => Type >= 0x40 ? (Type - 0x40) >> 2 : -1;
        public bool HasConversation => Dialog > 0 && Dialog < 0x80;
        public bool IsShopkeeper => Dialog >= 0x81 && Dialog <= 0x88;
        public bool IsGuard => Dialog == 0xff;
        public ShopTrade Trade => (ShopTrade)(Dialog - 0x81);
    }

    /// <summary>Shop types as indexed by the .NPC dialogue number (0x81 + trade) and DATA.OVL's shop tables.</summary>
    public enum ShopTrade { Armoury = 0, Tavern = 1, Horses = 2, Shipwright = 3, Reagents = 4, Guild = 5, Healer = 6, Inn = 7 }

    /// <summary>
    /// A .NPC file: 8 settlements × (32 schedules of 16 bytes, 32 type bytes, 32 dialogue bytes)
    /// = 8 × 576 = 4608 bytes.
    /// </summary>
    public class NpcFile
    {
        public const int Settlements = 8;
        public const int NpcsPerSettlement = 32;
        const int BlockSize = NpcsPerSettlement * 16 + NpcsPerSettlement * 2;

        readonly SettlementNpc[][] npcs = new SettlementNpc[Settlements][];

        public NpcFile(byte[] data)
        {
            if (data.Length != Settlements * BlockSize)
                throw new U5FormatException($".NPC file has {data.Length} bytes, expected {Settlements * BlockSize}");
            for (int s = 0; s < Settlements; s++)
            {
                int b = s * BlockSize;
                npcs[s] = new SettlementNpc[NpcsPerSettlement];
                for (int n = 0; n < NpcsPerSettlement; n++)
                {
                    int o = b + n * 16;
                    var sch = new NpcSchedule();
                    for (int k = 0; k < 3; k++)
                    {
                        sch.Ai[k] = data[o + k];
                        sch.X[k] = data[o + 3 + k];
                        sch.Y[k] = data[o + 6 + k];
                        sch.Z[k] = (sbyte)data[o + 9 + k];
                    }
                    for (int k = 0; k < 4; k++) sch.Times[k] = data[o + 12 + k];
                    npcs[s][n] = new SettlementNpc
                    {
                        Slot = n,
                        Schedule = sch,
                        Type = data[b + NpcsPerSettlement * 16 + n],
                        Dialog = data[b + NpcsPerSettlement * 17 + n],
                    };
                }
            }
        }

        /// <summary>All 32 slots of settlement <paramref name="slot"/> 0..7 (slot 0 is never a real NPC).</summary>
        public SettlementNpc[] Settlement(int slot) => npcs[slot];
    }
}
