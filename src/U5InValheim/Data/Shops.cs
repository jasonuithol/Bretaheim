using System;
using System.Collections.Generic;
using System.Text;

namespace U5InValheim.Data
{
    /// <summary>A shop as TALK.OVL talk_to_shopkeeper sets it up: its trade, its slot in g_shop_towns, its name and keeper.</summary>
    public class ShopInfo
    {
        public ShopTrade Trade;
        /// <summary>Index of the town in the trade's town list (0xb114), which indexes the price and stock tables.</summary>
        public int Slot;
        public Location Location;
        public string Name, Keeper;
    }

    /// <summary>What a shop sells: an Ultima item (or pack), how many and for how much.</summary>
    public class Ware
    {
        /// <summary>Equipment id (0..47), reagent (0x100 + n), or a special: 0x200 torch, 0x201 gem, 0x202 food.</summary>
        public int Item;
        public string Name;
        public int Quantity = 1;
        public int Price;
    }

    /// <summary>
    /// The shops of SHOPPES/SHOPPES2/SHOPPES3.OVL (Rust port rust/src/ovl/shoppes*.rs): who keeps
    /// them, when they open, their greetings and farewells (SHOPPE.DAT through print_shop_text)
    /// and their stock and prices (DATA.OVL tables).
    /// </summary>
    public class Shops
    {
        public const int Torch = 0x200, Gem = 0x201, Food = 0x202, ReagentBase = 0x100;

        // DATA.OVL (DGROUP)
        const int ShopWordPtrs = 0x23ea, GreetingOffsets = 0x3b2a, NothingBoughtOffsets = 0x3b6a, ThanksOffsets = 0x3baa,
            GuildPrices = 0x3bea, HealPrices = 0x3d86, CurePrices = 0x3d8e, ArmsHail = 0x3d46,
            RationPrices = 0x4c54, Morning = 0x7826, Afternoon = 0x782e, Evening = 0x7838, SaysKeeper = 0x785c,
            ArmsWelcome = 0x8018, ArmsSays = 0x8036, ComeSeeMe = 0x9196, WhenOpen = 0x91c4;
        const int ShoppeBlock = 0x5dc;

        readonly U5Data data;
        readonly byte[] shoppe;

        public Shops(U5Data data, byte[] shoppeDat)
        {
            this.data = data;
            shoppe = shoppeDat;
        }

        DataOvl Ovl => data.Ovl;

        public ShopInfo Of(Location loc, SettlementNpc npc)
        {
            if (!npc.IsShopkeeper) return null;
            var trade = npc.Trade;
            int slot = Math.Max(0, Ovl.ShopTowns(trade).IndexOf(loc.Number));
            int i = (int)trade * 0x20 + slot * 2;
            return new ShopInfo
            {
                Trade = trade, Slot = slot, Location = loc,
                Name = Ovl.CStr(Ovl.U16(Dg.ShopNamePtrs + i)).Trim(), Keeper = Ovl.CStr(Ovl.U16(Dg.ShopkeeperNamePtrs + i)).Trim(),
            };
        }

        /// <summary>Shops serve while the keeper is at work: schedule entry 1 (TALK talk_to_npc's "odd entry" test).</summary>
        public static bool IsOpen(SettlementNpc npc, int hour) => npc.Schedule.PlaceAt(hour) == 1;

        public string Closed(ShopInfo s) => (Ovl.CStr(ComeSeeMe) + s.Name + " " + Ovl.CStr(WhenOpen)).Replace("\n", " ").Trim();

        /// <summary>What the keeper says on arrival (arms: "Good @, and welcome to #!" and Buy or Sell; others a random greeting).</summary>
        public string Greeting(ShopInfo s, int hour, Random rnd)
        {
            if (s.Trade == ShopTrade.Armoury)
                return Expand(Bytes(ArmsWelcome), s, hour) + Expand(Bytes(ArmsSays), s, hour) + Ovl.PtrStr(ArmsHail, rnd.Next(2)) + "\"";
            return "\"" + ShoppeDat(Ovl.U16(GreetingOffsets + (int)s.Trade * 8 + rnd.Next(4) * 2), s, hour);
        }

        /// <summary>print_farewell: a thanks for buying, or a line for leaving with nothing, then "says Keeper."</summary>
        public string Farewell(ShopInfo s, bool bought, int hour, Random rnd)
        {
            int table = bought ? ThanksOffsets : NothingBoughtOffsets;
            return "\"" + ShoppeDat(Ovl.U16(table + (int)s.Trade * 8 + rnd.Next(4) * 2), s, hour).TrimEnd() + "\n" + Expand(Bytes(SaysKeeper), s, hour);
        }

        /// <summary>The thanks lines alone (the speech bubble on a purchase).</summary>
        public List<string> Thanks(ShopInfo s, int hour)
        {
            var r = new List<string>();
            for (int k = 0; k < 4; k++) r.Add(ShoppeDat(Ovl.U16(ThanksOffsets + (int)s.Trade * 8 + k * 2), s, hour).Replace("\n", " ").Trim().Trim('"'));
            return r;
        }

        public List<Ware> Stock(ShopInfo s)
        {
            var r = new List<Ware>();
            switch (s.Trade)
            {
                case ShopTrade.Armoury:
                    foreach (int id in Ovl.ArmouryStock(s.Location.Number))
                    {
                        int price = Ovl.ItemPrice(id);
                        if (price == 0) continue;
                        // arrows and quarrels come by the quiver (arms_buy_item fills to 99)
                        bool ammo = Ovl.ItemName(id).IndexOf("Arrow", StringComparison.OrdinalIgnoreCase) >= 0 || Ovl.ItemName(id).IndexOf("Quarrel", StringComparison.OrdinalIgnoreCase) >= 0;
                        r.Add(new Ware { Item = id, Name = Ovl.ItemName(id), Price = price, Quantity = ammo ? 20 : 1 });
                    }
                    break;
                case ShopTrade.Reagents:
                    for (int i = 0; i < 8; i++)
                    {
                        int price = Ovl.U8(Dg.ReagentPrices + s.Slot * 8 + i), qty = Ovl.U8(Dg.ReagentStock + s.Slot * 8 + i);
                        if (price != 0) r.Add(new Ware { Item = ReagentBase + i, Name = Ovl.ReagentName(i), Price = price, Quantity = Math.Max(1, qty) });
                    }
                    break;
                case ShopTrade.Guild:
                    // i16 [slot][4]: keys (3 for the price; dropped, no locks: items.json), gems by 4, torches by 5
                    r.Add(new Ware { Item = Gem, Name = "Gems", Price = Ovl.U16(GuildPrices + s.Slot * 8 + 1 * 2), Quantity = 4 });
                    r.Add(new Ware { Item = Torch, Name = "Torches", Price = Ovl.U16(GuildPrices + s.Slot * 8 + 2 * 2), Quantity = 5 });
                    break;
                case ShopTrade.Tavern:
                    int ration = Ovl.U16(RationPrices + s.Slot * 2);
                    if (ration > 0) r.Add(new Ware { Item = Food, Name = "Rations", Price = ration, Quantity = 25 });
                    break;
            }
            return r;
        }

        /// <summary>Healer's prices: heal and cure (0 = free, as in Minoc).</summary>
        public (int heal, int cure) HealerPrices(ShopInfo s) => (Ovl.U8(HealPrices + s.Slot), Ovl.U8(CurePrices + s.Slot));

        string ShoppeDat(int offset, ShopInfo s, int hour)
        {
            if (offset < 0 || offset >= shoppe.Length) return "";
            int end = offset;
            while (end < shoppe.Length && end - offset < ShoppeBlock && shoppe[end] != 0) end++;
            var b = new byte[end - offset];
            Buffer.BlockCopy(shoppe, offset, b, 0, b.Length);
            return Expand(b, s, hour);
        }

        byte[] Bytes(int dg)
        {
            var r = new List<byte>();
            for (int a = dg; Ovl.U8(a) != 0; a++) r.Add(Ovl.U8(a));
            return r.ToArray();
        }

        /// <summary>print_shop_text: literal text with macros (# shop, $ keeper, * town, @ time of day, % price, ^ quantity, &amp; item) and compressed words (≥ 0x80, with spaces around).</summary>
        public string Expand(byte[] text, ShopInfo s, int hour, int price = 0, int qty = 0, string item = "")
        {
            var sb = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                byte c = text[i];
                if (c >= 0x80)
                {
                    sb.Append(' ').Append(Ovl.PtrStr(ShopWordPtrs, c));
                    if (i + 1 < text.Length && text[i + 1] != 0 && text[i + 1] < 0x80) sb.Append(' ');
                    continue;
                }
                switch ((char)c)
                {
                    case '#': sb.Append(s.Name); break;
                    case '$': sb.Append(s.Keeper); break;
                    case '*': sb.Append(s.Location.Name); break;
                    case '&': sb.Append(item); break;
                    case '%': sb.Append(price); break;
                    case '^': sb.Append(qty); break;
                    case '@': sb.Append(Ovl.CStr(hour < 12 ? Morning : hour < 18 ? Afternoon : Evening)); break;
                    default: sb.Append((char)c); break;
                }
            }
            return sb.ToString();
        }
    }
}
