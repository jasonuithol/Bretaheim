using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace U5InValheim.Data
{
    /// <summary>One of a tavern's four offers (the keys of its menu style).</summary>
    public enum TavernOffer { Meal, Drink, Rations, Rumour }

    /// <summary>A rumour the barkeep sells: the keyword's price, then who to seek and where.</summary>
    public class Rumour
    {
        public int Index, Price;
        public string Keyword, Person, Place;
    }

    /// <summary>
    /// Taverns (SHOPPES2.OVL tavern_shop, order_meal, order_drink, serve_round_or_meal,
    /// buy_rumour: rust/src/ovl/shoppes2.rs): each tavern has a menu style (Mutton/Ale/Rations/Chat,
    /// Cheese/Wines/–/Talk, Boar/Rum/–/Help, Fruits/Stout/Provisions/Advice), a meal price, a round
    /// of drink at 1 gp a head (or the wine list), and rumours sold once something was bought.
    /// </summary>
    public partial class Shops
    {
        const int TavernStyles = 0x4d4c, MealKeys = 0x4c1e, DrinkKeys = 0x4c24, RationKeys = 0x4c2a, RumourKeys = 0x4c30,
            MealPrices = 0x4c36, WinePrices = 0x4c48, MenuTexts = 0x4d56, MoreTexts = 0x4d5e,
            RumourWords = 0x4c74, RumourPrices = 0x4d10, RumourPeople = 0x4ca8, RumourPlaceIndex = 0x4cdc, RumourPlaces = 0x4cf6, RumourReplies = 0x4d44,
            ThatWillBe = 0x9ace, GoldForThe = 0x9adc, CantPay = 0x9af6, Enjoy = 0x9b16, BegPardon = 0x9b22, EnoughToDrink = 0x9b42,
            WineList = 0x9b76, WineChoice = 0x9bfa, FineChoice = 0x9c08, AnythingElseTavern = 0x9f9a,
            Lore = 0x9f00, CannotHelp = 0x9f3a, Hazy = 0x134e, FairNuff = 0x9f5c, Sorry = 0x9f78, PayingCustomers = 0x146a, MeagreStock = 0x1413;
        static readonly int[] WineLines = { 0x9b8c, 0x9b9e, 0x9bb0, 0x9bc2, 0x9bd4, 0x9be6 };
        public const int Rumours = 26, Wines = 6;
        /// <summary>Rounds of drink after which the keeper asks "But haven't ye had enough to drink?".</summary>
        public const int RoundsBeforeEnough = 3;
        /// <summary>Moves a party stays drunk (g_drunk_moves = 25) after insisting on another round.</summary>
        public const int DrunkMoves = 25;

        /// <summary>The tavern's menu style 0..3 (g_tavern_style_table[slot]).</summary>
        public int TavernStyle(ShopInfo s) => Ovl.U8(TavernStyles + s.Slot);

        /// <summary>The key letter of an offer in this tavern's style (' ': not offered).</summary>
        public char OfferKey(ShopInfo s, TavernOffer offer)
        {
            int table = offer == TavernOffer.Meal ? MealKeys : offer == TavernOffer.Drink ? DrinkKeys : offer == TavernOffer.Rations ? RationKeys : RumourKeys;
            return (char)Ovl.U8(table + TavernStyle(s));
        }

        public bool Offers(ShopInfo s, TavernOffer offer) => OfferKey(s, offer) != ' ' && OfferKey(s, offer) != 0;

        /// <summary>"What'll it be... a leg of our tender roast Mutton, a tankard of Ale, or Rations for thy travels?"</summary>
        public string TavernMenu(ShopInfo s, int hour) => "\"" + Dat(Ovl.U16(MenuTexts + TavernStyle(s) * 2), s, hour) + "\"";

        /// <summary>The style's follow-up after "Anything else for thee?" Yes ("Shall I bring thee more Ale or roast Mutton, or wouldst thou rather Chat? …").</summary>
        public string TavernMore(ShopInfo s, int hour) => "\"" + Dat(Ovl.U16(MoreTexts + TavernStyle(s) * 2), s, hour) + "\"";

        /// <summary>The word the menu names an offer by (its capitalised word starting with the key: Mutton, Ale, Rations, Chat), else the key.</summary>
        public string OfferName(ShopInfo s, TavernOffer offer, int hour)
        {
            char key = OfferKey(s, offer);
            foreach (var text in new[] { TavernMenu(s, hour), TavernMore(s, hour) })
            {
                var m = Regex.Match(text, @"\b" + Regex.Escape(key.ToString()) + @"[a-z]+");
                if (m.Success) return m.Value;
            }
            return key.ToString();
        }

        /// <summary>A meal for one (g_tavern_meal_prices[slot]); the party's meal is this per living member.</summary>
        public int MealPrice(ShopInfo s) => (short)Ovl.U16(MealPrices + s.Slot * 2);

        /// <summary>A round of drink: 1 gp a head.</summary>
        public const int RoundPrice = 1;

        /// <summary>Taverns whose drink is Wine show the wine list.</summary>
        public bool HasWineList(ShopInfo s) => OfferKey(s, TavernOffer.Drink) == 'W';

        /// <summary>The wine list: names and prices (Rose 18, Claret 192, Sauterne 79, Muscatel 30, Moselle 275, Chablis 98).</summary>
        public List<(string name, int price)> WineListEntries()
        {
            var r = new List<(string, int)>();
            for (int i = 0; i < Wines; i++)
            {
                var line = Ovl.CStr(WineLines[i]).Trim();
                var name = Regex.Replace(line.Substring(line.IndexOf(')') + 1), @"\.+\s*\d+\s*$", "").Trim();
                r.Add((name, (short)Ovl.U16(WinePrices + i * 2)));
            }
            return r;
        }

        static string Sir(DataOvl ovl, bool female) => ovl.CStr(female ? 0x9ac6 : 0x9ac2);

        /// <summary>"Our wine list, sir." with its lines and "Thy choice?".</summary>
        public string WineListText(bool female)
        {
            var sb = new System.Text.StringBuilder(Ovl.CStr(WineList).Replace("\n", " ") + Sir(Ovl, female) + ".");
            foreach (int a in WineLines) sb.Append("\n").Append(Ovl.CStr(a).Trim());
            sb.Append("\n").Append(Ovl.CStr(WineChoice).Trim());
            return sb.ToString();
        }

        /// <summary>"Ah, a fine choice, sir."</summary>
        public string FineWine(bool female) => "\"" + Ovl.CStr(FineChoice).Replace("\n", " ").Trim().TrimStart('"') + Sir(Ovl, female) + ".\"";

        /// <summary>"Well! Our meagre stock is not good enough for thee!" (leaving the wine list).</summary>
        public string MeagreStockText(ShopInfo s, int hour) => Dat(MeagreStock, s, hour);

        /// <summary>serve_round_or_meal: "That will be N gold for the [two..six] of ye, sir."</summary>
        public string ThatWillBeText(int price, int living, bool female)
        {
            string count = living >= 2 && living <= 6 ? new[] { "two", "three", "four", "five", "six" }[living - 2] : "";
            return "\"" + Ovl.CStr(ThatWillBe) + price + Ovl.CStr(GoldForThe) + count + Ovl.CStr(0x9aec).Replace("\n", " ") + Sir(Ovl, female) + ".";
        }

        /// <summary>"CAN'T PAY? Beat it!" yells the keeper.</summary>
        public string CantPayText(ShopInfo s) => Ovl.CStr(CantPay).Replace("\n", " ").Trim().TrimStart('"').Trim() + s.Keeper + ".";

        /// <summary>"Enjoy!"</summary>
        public string EnjoyText => "\"" + Ovl.CStr(Enjoy).Replace("\n", " ").Trim();

        /// <summary>"I beg thy pardon, sir," says the keeper. "But haven't ye had enough to drink?"</summary>
        public string EnoughText(ShopInfo s, bool female)
            => Ovl.CStr(BegPardon).Replace("\n", " ").Trim() + Sir(Ovl, female) + ",\" says " + s.Keeper + Ovl.CStr(EnoughToDrink).Replace("\n", " ").Trim();

        /// <summary>"Anything else for thee?"</summary>
        public string AnythingElseForThee => Ovl.CStr(AnythingElseTavern).Replace("\n", " ").Trim();

        // ── rumours (buy_rumour) ────────────────────────────────────────────────────────────

        /// <summary>"Of what wouldst thou hear my lore, sir?"</summary>
        public string LoreQuestion(bool female) => "\"" + Ovl.CStr(Lore).Replace("\n", " ") + Sir(Ovl, female) + "?\"";

        /// <summary>"That, I cannot help thee with."</summary>
        public string CannotHelpText => Ovl.CStr(CannotHelp).Replace("\n", " ").Trim();

        /// <summary>
        /// The rumour asked for: the first of the 26 four-letter keywords found in the answer
        /// (case-insensitively) at its start or right after a space, or null. The original reads at
        /// most 15 characters.
        /// </summary>
        public Rumour FindRumour(string answer)
        {
            if (string.IsNullOrEmpty(answer)) return null;
            if (answer.Length > 15) answer = answer.Substring(0, 15);
            for (int i = 0; i < Rumours; i++)
            {
                string kw = Ovl.PtrStr(RumourWords, i);
                int pos = answer.IndexOf(kw, StringComparison.OrdinalIgnoreCase);
                if (pos == 0 || (pos > 0 && answer[pos - 1] == ' '))
                    return new Rumour
                    {
                        Index = i, Keyword = kw, Price = (short)Ovl.U16(RumourPrices + i * 2),
                        Person = Ovl.PtrStr(RumourPeople, i), Place = Ovl.PtrStr(RumourPlaces, Ovl.U8(RumourPlaceIndex + i)),
                    };
            }
            return null;
        }

        /// <summary>"Well now, my memory on that subject is a bit hazy. Mayhap N gold crowns …" "Fair 'nuff?"</summary>
        public string RumourPrice(ShopInfo s, int hour, Rumour r) => Dat(Hazy, s, hour, r.Price) + " " + Ovl.CStr(FairNuff).Replace("\n", " ").Trim();

        /// <summary>"Sorry, sir, I only attend to PAYING customers!" says the keeper.</summary>
        public string OnlyPayingCustomers(ShopInfo s, int hour, bool female) => Ovl.CStr(Sorry) + Sir(Ovl, female) + Dat(PayingCustomers, s, hour);

        /// <summary>One of the four answers ("Seek out &amp; in *!" …) with who and where, and "says" the keeper.</summary>
        public string RumourAnswer(ShopInfo s, int hour, Rumour r, Random rnd)
        {
            int offset = Ovl.U16(RumourReplies + rnd.Next(4) * 2);
            var text = ShoppeDatWith(offset, s, hour, r.Person, r.Place);
            return text.Replace("\n", " ").Trim() + " says " + s.Keeper + ".";
        }

        /// <summary>SHOPPE.DAT text with '&amp;' and '*' set (buy_rumour points them at the person and the place).</summary>
        string ShoppeDatWith(int offset, ShopInfo s, int hour, string item, string place)
        {
            var at = new ShopInfo { Trade = s.Trade, Slot = s.Slot, Name = s.Name, Keeper = s.Keeper, Location = new Location { Name = place ?? s.Location?.Name ?? "" } };
            return ShoppeDat(offset, at, hour, 0, item);
        }
    }
}
