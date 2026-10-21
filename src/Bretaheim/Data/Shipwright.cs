using System;

namespace Bretaheim.Data
{
    /// <summary>What the shipwright does with a purchase (SHOPPES2 shipwright_menu), from the vessel already bought on this visit.</summary>
    public enum VesselSale
    {
        /// <summary>A frigate (with two skiffs aboard) for the dock.</summary>
        Frigate,
        /// <summary>A skiff for the dock.</summary>
        Skiff,
        /// <summary>A skiff put aboard the frigate just bought ("It will be placed onboard thy frigate!").</summary>
        SkiffAboard,
        /// <summary>A second skiff without a ship: refused, nothing paid ("More than one skiff …").</summary>
        SkiffRefused,
        /// <summary>A second frigate: "our dock space is limited … 87,000 gold. Acceptable?" (never affordable).</summary>
        DockFull,
    }

    /// <summary>
    /// The shipwright and the stables (SHOPPES2.OVL shipwright_shop/shipwright_menu/shipwright_deliver,
    /// SHOPPES.OVL horse_shop: rust/src/ovl/shoppes2.rs, shoppes.rs): prices, the docks a vessel is
    /// delivered to, the sale rules and the keepers' words (SHOPPE.DAT).
    /// </summary>
    public partial class Shops
    {
        const int FrigatePrices = 0x4d66, SkiffPrices = 0x4d6e, HorsePrices = 0x3c30,
            CheatMe = 0x198e, Yells = 0x9fc2, SoldDock = 0x19ab, AnythingElseShip = 0x19da, ShipSir = 0x9fd4, ShipQuestion = 0x9fd8,
            ShipMenu = 0x18eb, FrigatePitch = 0x183e, TakeIt = 0x1a50, DockLimited = 0x19f2, SkiffPitch = 0x188c,
            OneSkiff = 0x195f, PlacedAboard = 0x193b,
            StablesClosedText = 0x7a48, HorsePitch = 0x1643, DealQ = 0x7a6a, CantAfford = 0x7a7e, FeedIt = 0x7a9e, HorseOut = 0x9072;

        /// <summary>The bought-vessel byte (SAVED.GAM 0x105F): 0x40 a skiff, 0x80 + n a frigate with n skiffs aboard.</summary>
        public const int BoughtSkiff = 0x40, BoughtFrigate = 0x82;
        /// <summary>The 87,000 gold the shipwright asks for a second frigate: checked against 10,000 gold, which nobody carries.</summary>
        public const int DockFullPrice = 10000;
        /// <summary>A delivered vessel's hull (value1 = 99, run_overworld).</summary>
        public const int NewHull = 99;

        /// <summary>
        /// SHOPPES/SHOPPES2's intelligence adjustment: price + price·(100 − 3·INT)/100, a 32-bit
        /// multiply and an unsigned divide into a 16-bit price (above 33 the original's garbage, kept).
        /// </summary>
        public static int AdjustForIntelligence(int price, int intelligence)
        {
            int factor = 100 - 3 * intelligence;
            uint product = unchecked((uint)(factor * price));
            uint q = product / 100;
            return unchecked((short)(price + (short)q));
        }

        /// <summary>A frigate's price at this shipwright (600/753/650/700) for this intelligence.</summary>
        public int FrigatePrice(ShopInfo s, int intelligence) => AdjustForIntelligence((short)Ovl.U16(FrigatePrices + s.Slot * 2), intelligence);

        /// <summary>A skiff's price (200/175/125/100) for this intelligence.</summary>
        public int SkiffPrice(ShopInfo s, int intelligence) => AdjustForIntelligence((short)Ovl.U16(SkiffPrices + s.Slot * 2), intelligence);

        /// <summary>A horse's price at these stables (100/130/160/190) for this intelligence.</summary>
        public int HorsePrice(ShopInfo s, int intelligence) => AdjustForIntelligence((short)Ovl.U16(HorsePrices + s.Slot * 2), intelligence);

        /// <summary>The overworld cell a bought vessel is delivered to (g_dock_x/y[slot]).</summary>
        public (int x, int y) Dock(ShopInfo s) => (Ovl.U8(Dg.DockX + s.Slot), Ovl.U8(Dg.DockY + s.Slot));

        /// <summary>shipwright_menu: the sale of a frigate or a skiff given the vessel bought on this visit so far.</summary>
        public static VesselSale Sell(int bought, bool frigate)
        {
            if (frigate) return (bought & 0xc0) == 0 ? VesselSale.Frigate : VesselSale.DockFull;
            if ((bought & 0xc0) == 0) return VesselSale.Skiff;
            return (bought & 0x80) == 0 ? VesselSale.SkiffRefused : VesselSale.SkiffAboard;
        }

        /// <summary>The bought-vessel byte after a sale.</summary>
        public static int After(int bought, VesselSale sale)
        {
            switch (sale)
            {
                case VesselSale.Frigate: return BoughtFrigate;
                case VesselSale.Skiff: return BoughtSkiff;
                case VesselSale.SkiffAboard: return (bought + 1) & 0xff;
                default: return bought;
            }
        }

        /// <summary>Whether the sale is paid for (a refused second skiff costs nothing; a second frigate is never sold).</summary>
        public static bool Paid(VesselSale sale) => sale == VesselSale.Frigate || sale == VesselSale.Skiff || sale == VesselSale.SkiffAboard;

        /// <summary>The skiffs aboard a frigate of this bought-vessel byte (value3 = byte &amp; 0x3f).</summary>
        public static int SkiffsAboard(int bought) => bought >= 0x80 ? bought & 0x3f : 0;

        // ── the shipwright's words ──────────────────────────────────────────────────────────

        string Dat(int offset, ShopInfo s, int hour, int price = 0) => ShoppeDat(offset, s, hour, price).Replace("\n", " ").Trim();
        string Text(int dg, ShopInfo s, int hour) => Expand(Bytes(dg), s, hour).Replace("\n", " ").Trim();

        /// <summary>"We sell ocean-going Frigates and small, light Skiffs. Which would ye like to see?"</summary>
        public string ShipwrightMenu(ShopInfo s, int hour) => Dat(ShipMenu, s, hour);

        /// <summary>The frigate's pitch with its price and "Wilt thou take it?".</summary>
        public string FrigateOffer(ShopInfo s, int hour, int price) => "\"" + Dat(FrigatePitch, s, hour, price) + " " + Dat(TakeIt, s, hour);

        /// <summary>The skiff's pitch with its price and "Wilt thou take it?".</summary>
        public string SkiffOffer(ShopInfo s, int hour, int price) => "\"" + Dat(SkiffPitch, s, hour, price) + " " + Dat(TakeIt, s, hour);

        /// <summary>"What? Cheat me, would ye? OUT!" yells the shipwright.</summary>
        public string CheatMeOut(ShopInfo s, int hour) => Dat(CheatMe, s, hour) + " " + Text(Yells, s, hour);

        /// <summary>"Sold! Thou may take delivery at the docks outside the city!"</summary>
        public string SoldAtDocks(ShopInfo s, int hour) => Dat(SoldDock, s, hour) + "\"";

        /// <summary>"Will there be anything else, sir?" (the original asks a byte that never says milady: always "sir").</summary>
        public string AnythingElse(ShopInfo s, int hour) => "\"" + Dat(AnythingElseShip, s, hour) + Ovl.CStr(ShipSir) + Ovl.CStr(ShipQuestion).Trim();

        /// <summary>The words for a sale other than a plain frigate or skiff.</summary>
        public string SaleWords(ShopInfo s, VesselSale sale, int hour)
        {
            switch (sale)
            {
                case VesselSale.SkiffAboard: return Dat(PlacedAboard, s, hour);
                case VesselSale.SkiffRefused: return Dat(OneSkiff, s, hour);
                case VesselSale.DockFull: return Dat(DockLimited, s, hour);
                default: return SoldAtDocks(s, hour);
            }
        }

        // ── the stables ─────────────────────────────────────────────────────────────────────

        public string StablesClosed => Ovl.CStr(StablesClosedText).Trim();

        /// <summary>"Thou canst hardly find better horses at lower prices. N gp is truly unworthy of these magnificent creatures!" "Deal?"</summary>
        public string HorseOffer(ShopInfo s, int hour, int price) => "\"" + Dat(HorsePitch, s, hour, price) + " " + Text(DealQ, s, hour);

        /// <summary>"Thou couldst not afford to feed it!" yells the keeper.</summary>
        public string CannotFeedIt(ShopInfo s, int hour) => Text(CantAfford, s, hour) + " " + Text(FeedIt, s, hour);

        /// <summary>A merchant says: "GET THAT HORSE OUT OF HERE!" (any shop but the stables, on horseback).</summary>
        public string HorseOutOfHere => Ovl.CStr(HorseOut).Replace("\n", " ").Trim();
    }
}
