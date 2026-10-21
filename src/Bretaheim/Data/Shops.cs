using System;
using System.Collections.Generic;
using System.Text;

namespace Bretaheim.Data
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
    public partial class Shops
    {
        public const int Torch = 0x200, Gem = 0x201, Food = 0x202, ReagentBase = 0x100;

        // DATA.OVL (DGROUP)
        const int ShopWordPtrs = 0x23ea, GreetingOffsets = 0x3b2a, NothingBoughtOffsets = 0x3b6a, ThanksOffsets = 0x3baa,
            GuildPrices = 0x3bea, HealPrices = 0x3d86, CurePrices = 0x3d8e, ArmsHail = 0x3d46,
            RationPrices = 0x4c54, Morning = 0x7826, Afternoon = 0x782e, Evening = 0x7838, SaysKeeper = 0x785c,
            ArmsWelcome = 0x8018, ArmsSays = 0x8036, ComeSeeMe = 0x9196, WhenOpen = 0x91c4,
            SellOffers = 0x3cbe, SellNames = 0x3cce, LongNames = 0x17f6, SellWhat = 0x3d2e, SellElse = 0x3d3e,
            NoAmmo = 0x7d32, Done = 0x7d76, CannotBuy = 0x7d8c, NothingToSell = 0x7f20;
        /// <summary>The Avatar's intelligence until characters have Ultima stats (Phase 5).</summary>
        public const int DefaultIntelligence = 15;
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
                    // (menu styles 1 and 2 sell no rations: their rations key is blank)
                    if (ration > 0 && Offers(s, TavernOffer.Rations)) r.Add(new Ware { Item = Food, Name = "Rations", Price = ration, Quantity = 25 });
                    break;
            }
            return r;
        }

        // ── selling arms (SHOPPES arms_sell_browser / arms_sell_item) ──────────────────────

        /// <summary>Arrows and quarrels: "We don't deal in used ammunition!"</summary>
        public static bool IsAmmo(int item) => item == 0x1b || item == 0x1d;

        /// <summary>What the armourer offers: intelligence × base price (16-bit) × 3 / 100 + 1; 0 when they cannot buy it.</summary>
        public int SellOffer(int item, int intelligence)
        {
            int price = item >= 0 && item < 0x30 ? Ovl.ItemPrice(item) : 0;
            if (price == 0) return 0;
            return (int)((ushort)(intelligence * price) * 3u / 100) + 1;
        }

        public string SellPrompt(Random rnd) => "\"" + Ovl.PtrStr(SellWhat, rnd.Next(4)) + "\"";
        public string SellMore(Random rnd) => "\"" + Ovl.PtrStr(SellElse, rnd.Next(4)) + "\"";

        /// <summary>A random offer ("For thy admittedly inferior &amp;, I will pay thee % gp.") and "Deal?"</summary>
        public string Offer(ShopInfo s, int item, int price, int hour, Random rnd)
            => "\"" + ShoppeDat(Ovl.U16(SellOffers + rnd.Next(8) * 2), s, hour, price, SellName(item)).Trim() + " Deal?\"";

        public string Sold(ShopInfo s, int hour) => Expand(Bytes(Done), s, hour).Replace("Yes", "").Trim();
        public string WillNotBuy(ShopInfo s, int item, int hour)
            => (IsAmmo(item) ? Expand(Bytes(NoAmmo), s, hour) : "\"" + Expand(Bytes(CannotBuy), s, hour)).Trim();
        public string NoWares(ShopInfo s, int hour) => "\"" + Expand(Bytes(NothingToSell), s, hour).Trim();

        /// <summary>The name an armourer calls an item by (0x3cce, else the long name).</summary>
        public string SellName(int item)
        {
            int p = Ovl.U16(SellNames + item * 2);
            return Ovl.CStr(p != 0 ? p : Ovl.U16(LongNames + item * 2)).Trim();
        }

        // ── the inn (SHOPPES3 inn_shop: rest for the night) ─────────────────────────────────
        const int InnRates = 0x4d7e, InnRooms = 0x4dc4, InnRoomTexts = 0x4e6e, InnBedX = 0x4e7a, InnBedY = 0x4e80,
            InnTakeIt = 0x4dca, InnHighwaymen = 0x4de3, InnScreams = 0x4e07, InnPleasant = 0x4e13, InnMilady = 0x4e2c,
            InnSir = 0x4e33, InnSays = 0x4e37, InnZzz = 0x4e44, InnMorning = 0x4e51;

        /// <summary>
        /// A night's rest for one (rate × party, here one player) adjusted for intelligence as the
        /// original (SHOPPES3 adjust_price_for_intelligence): price + price·(100 − 3·INT)/100, the
        /// division signed — dearer below 34 intelligence, cheaper above.
        /// </summary>
        public int InnPrice(ShopInfo s, int intelligence)
        {
            int rate = Ovl.U8(InnRates + s.Slot);
            return rate + rate * (100 - 3 * intelligence) / 100;
        }
        // (the other shops' adjustment, the same with an unsigned divide: AdjustForIntelligence)

        /// <summary>How many guests the inn takes.</summary>
        public int InnRoomCount(ShopInfo s) => Ovl.U8(InnRooms + s.Slot);

        /// <summary>The inn's bed (the cell the party is put to sleep in).</summary>
        public (int x, int y) InnBed(ShopInfo s) => (Ovl.U8(InnBedX + s.Slot), Ovl.U8(InnBedY + s.Slot));

        /// <summary>The room offered, with its price, and "Wilt thou take it?".</summary>
        public string InnRoom(ShopInfo s, int hour, int price)
            => "\"" + ShoppeDat(Ovl.U16(InnRoomTexts + s.Slot * 2), s, hour, price).Trim() + " " + Expand(Bytes(InnTakeIt), s, hour).Trim();

        /// <summary>"Highwaymen! Cheap, at that! OUT!" screams the keeper (no money).</summary>
        public string InnThrownOut(ShopInfo s, int hour) => Expand(Bytes(InnHighwaymen), s, hour).Trim() + " " + Expand(Bytes(InnScreams), s, hour).Trim();

        /// <summary>"Have a pleasant night, sir!" says the keeper.</summary>
        public string InnGoodNight(ShopInfo s, int hour, bool female)
            => Expand(Bytes(InnPleasant), s, hour) + Ovl.CStr(female ? InnMilady : InnSir) + Expand(Bytes(InnSays), s, hour).TrimEnd();

        public string Zzz => Ovl.CStr(InnZzz).Trim();
        public string MorningCry => Ovl.CStr(InnMorning).Trim();

        /// <summary>Healer's prices: heal and cure (0 = free, as in Minoc).</summary>
        public (int heal, int cure) HealerPrices(ShopInfo s) => (Ovl.U8(HealPrices + s.Slot), Ovl.U8(CurePrices + s.Slot));

        string ShoppeDat(int offset, ShopInfo s, int hour, int price = 0, string item = "")
        {
            if (offset < 0 || offset >= shoppe.Length) return "";
            int end = offset;
            while (end < shoppe.Length && end - offset < ShoppeBlock && shoppe[end] != 0) end++;
            var b = new byte[end - offset];
            Buffer.BlockCopy(shoppe, offset, b, 0, b.Length);
            return Expand(b, s, hour, price, 0, item);
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
