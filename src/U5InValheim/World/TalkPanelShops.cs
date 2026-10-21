using System;
using System.Collections.Generic;
using System.Linq;
using U5InValheim.Data;
using UnityEngine;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// The shipwright, the stables and the tavern's table service in the talk panel (SHOPPES2
    /// shipwright_shop/tavern_shop, SHOPPES horse_shop): their keepers' words from SHOPPE.DAT, a
    /// button for each of the original's keys, Yes/No for its questions. A frigate or skiff is
    /// delivered to the town's dock (<see cref="Vessels"/>), a horse is led out beside the buyer
    /// (<see cref="Horses"/>); meals are barley rations, rounds of drink count toward "haven't ye
    /// had enough?", and rumours are sold once something was bought.
    /// </summary>
    public static partial class TalkPanel
    {
        /// <summary>Rounds of drink served (0xbd20: the original never resets it).</summary>
        static int rounds;

        static bool TransportShop(ShopInfo shop) => shop.Trade == ShopTrade.Shipwright || shop.Trade == ShopTrade.Horses || shop.Trade == ShopTrade.Tavern;

        static bool Female => Player.m_localPlayer?.GetComponent<VisEquipment>()?.GetModelIndex() == 1;
        static int Gold => Player.m_localPlayer?.GetInventory().CountItems("$item_coins") ?? 0;

        static void Pay(int price, bool surcharge = true)
        {
            var me = Player.m_localPlayer;
            if (me == null) return;
            if (price > 0) me.GetInventory().RemoveItem("$item_coins", price);
            if (surcharge && visit != null) Shadowlords.Cheat(me, visit.Location.Number);
        }

        /// <summary>On horseback, every merchant but the stables': "GET THAT HORSE OUT OF HERE!" (TALK talk_to_shopkeeper).</summary>
        static bool RefuseRider(TownNpc who, ShopInfo shop)
        {
            var me = Player.m_localPlayer;
            if (me == null || shop.Trade == ShopTrade.Horses || !Horses.Riding(me)) return false;
            me.Message(MessageHud.MessageType.Center, Plugin.U5.Shops.HorseOutOfHere);
            who.EndTalk();
            return true;
        }

        /// <summary>After the greeting: the shipwright's "Which would ye like to see?", the tavern's menu.</summary>
        static void ShopOpened(ShopInfo shop)
        {
            var shops = Plugin.U5.Shops;
            if (shop.Trade == ShopTrade.Shipwright) Show("\"" + shops.ShipwrightMenu(shop, TownNpc.Hour()).Trim('"').Trim() + "\"");
            if (shop.Trade == ShopTrade.Tavern) Show(shops.TavernMenu(shop, TownNpc.Hour()));
        }

        static void TransportChoices(TownNpc who, ShopInfo shop, List<(string, Action)> choices)
        {
            var shops = Plugin.U5.Shops;
            int hour = TownNpc.Hour();
            switch (shop.Trade)
            {
                case ShopTrade.Shipwright:
                    choices.Add(($"Frigate ({shops.FrigatePrice(shop, Intelligence)} gp)", () => ShowVessel(true)));
                    choices.Add(($"Skiff ({shops.SkiffPrice(shop, Intelligence)} gp)", () => ShowVessel(false)));
                    break;
                case ShopTrade.Horses:
                    choices.Add(($"Horse ({shops.HorsePrice(shop, Intelligence)} gp)", ShowHorse));
                    break;
                case ShopTrade.Tavern:
                    // the trade window sells the rations; the rest is served at the table
                    int buy = choices.FindIndex(c => c.Item1 == "Buy");
                    if (buy >= 0) choices[buy] = (shops.OfferName(shop, TavernOffer.Rations, hour), choices[buy].Item2);
                    if (shops.Offers(shop, TavernOffer.Meal)) choices.Insert(0, ($"{shops.OfferName(shop, TavernOffer.Meal, hour)} ({shops.MealPrice(shop)} gp)", Meal));
                    if (shops.Offers(shop, TavernOffer.Drink)) choices.Insert(1, (shops.OfferName(shop, TavernOffer.Drink, hour), Drink));
                    // (rumours only for a paying customer: the key does nothing before)
                    if (bought && shops.Offers(shop, TavernOffer.Rumour)) choices.Add((shops.OfferName(shop, TavernOffer.Rumour, hour), Rumour));
                    break;
            }
        }

        /// <summary>A question with Yes and No.</summary>
        static void YesNo(Action yes, Action no) => SetButtons(("Yes", yes), ("No", no));

        /// <summary>Thrown out ("OUT!", "Beat it!"): no farewell.</summary>
        static void ThrownOut(string words)
        {
            Show(words);
            SetButtons(("Bye", () => Close(farewell: false)));
        }

        // ── the shipwright ──────────────────────────────────────────────────────────────────

        static void ShowVessel(bool frigate)
        {
            var shop = visit;
            var shops = Plugin.U5.Shops;
            int hour = TownNpc.Hour();
            var me = Player.m_localPlayer;
            if (me == null) return;
            var sale = Shops.Sell(Vessels.BoughtHere(shop.Location.Number), frigate);
            if (sale == VesselSale.DockFull)
            {
                // "… The delivery price would be 87,000 gold. Acceptable?" Yes: nobody has it (OUT!); No: the menu again
                Show("\"" + shops.SaleWords(shop, sale, hour).Trim('"').Trim() + "\"");
                YesNo(() => ThrownOut(shops.CheatMeOut(shop, hour)), ShopButtons);
                return;
            }
            int price = frigate ? shops.FrigatePrice(shop, Intelligence) : shops.SkiffPrice(shop, Intelligence);
            Show(frigate ? shops.FrigateOffer(shop, hour, price) : shops.SkiffOffer(shop, hour, price));
            YesNo(() =>
            {
                if (Gold < price) { ThrownOut(shops.CheatMeOut(shop, hour)); return; }
                int before = Vessels.BoughtHere(shop.Location.Number);
                Vessels.Sold(shop.Location.Number, sale);
                if (Shops.Paid(sale))
                {
                    if (sale != VesselSale.SkiffAboard && !Vessels.Deliver(me, shop, frigate, out string why))
                    {
                        Vessels.Unsold(shop.Location.Number, before);
                        Show("(" + why + ")");
                        ShopButtons();
                        return;
                    }
                    if (sale == VesselSale.SkiffAboard) Vessels.AddSkiffAboard(me, shop.Location.Number);
                    Pay(price);
                    bought = true;
                }
                Show(sale == VesselSale.Frigate || sale == VesselSale.Skiff ? shops.SoldAtDocks(shop, hour) : shops.SaleWords(shop, sale, hour));
                // "Will there be anything else, sir?"
                Show(shops.AnythingElse(shop, hour));
                YesNo(ShopButtons, () => Close());
            }, () => { Show("No"); ShopButtons(); });
        }

        // ── the stables ─────────────────────────────────────────────────────────────────────

        static void ShowHorse()
        {
            var shop = visit;
            var shops = Plugin.U5.Shops;
            int hour = TownNpc.Hour();
            int price = shops.HorsePrice(shop, Intelligence);
            Show(shops.HorseOffer(shop, hour, price));
            YesNo(() =>
            {
                var me = Player.m_localPlayer;
                if (me == null) return;
                if (Gold < price) { Show(shops.CannotFeedIt(shop, hour)); SetButtons(("Bye", () => Close())); return; }
                if (!Horses.Buy(me, out string where)) { Show(shops.StablesClosed); SetButtons(("Bye", () => Close())); return; }
                Pay(price);
                bought = true;
                Show($"(Thy horse waits {where}. E to mount it.)");
                SetButtons(("Bye", () => Close()));
            }, () => { Show("No"); SetButtons(("Bye", () => Close())); });
        }

        // ── the tavern ──────────────────────────────────────────────────────────────────────

        /// <summary>"Anything else for thee?" Yes: the style's follow-up and the offers again; No: the farewell.</summary>
        static void TavernAnythingElse()
        {
            var shops = Plugin.U5.Shops;
            Show(shops.AnythingElseForThee);
            YesNo(() => { Show(shops.TavernMore(visit, TownNpc.Hour())); ShopButtons(); }, () => Close());
        }

        /// <summary>serve_round_or_meal: the bill, "CAN'T PAY? Beat it!" or paid and "Enjoy!".</summary>
        static bool Serve(int price)
        {
            var shops = Plugin.U5.Shops;
            Show(shops.ThatWillBeText(price, 1, Female));
            if (Gold < price) { ThrownOut(shops.CantPayText(visit)); return false; }
            Pay(price);
            Show(shops.EnjoyText);
            return true;
        }

        /// <summary>A meal: a ration (barley) for the one who eats (food + 1 a living member).</summary>
        static void Meal()
        {
            if (!Serve(Plugin.U5.Shops.MealPrice(visit))) return;
            var me = Player.m_localPlayer;
            var barley = ObjectDB.instance?.GetItemPrefab(Rations.Item);
            if (me != null && barley != null && !me.GetInventory().AddItem(barley, 1))
                Treasure.DropOnGround(barley, 1, me.transform.position + me.transform.forward);
            bought = true;
            TavernAnythingElse();
        }

        /// <summary>A round of drink (1 gp), the wine list where they serve wine; after the third round "haven't ye had enough?".</summary>
        static void Drink()
        {
            var shops = Plugin.U5.Shops;
            if (rounds == Shops.RoundsBeforeEnough)
            {
                Show(shops.EnoughText(visit, Female));
                YesNo(() => { bought = true; TavernAnythingElse(); }, () =>
                {
                    // "No!": drunk for 25 moves, a point of karma lost, and served anyway
                    var me = Player.m_localPlayer;
                    KarmaDeeds.Change(me, -1, "drunk");
                    Horses.Drunk(Shops.DrunkMoves);
                    Pour();
                });
                return;
            }
            Pour();
        }

        static void Pour()
        {
            var shops = Plugin.U5.Shops;
            if (!shops.HasWineList(visit))
            {
                if (!Serve(Shops.RoundPrice)) return;
                rounds++;
                bought = true;
                TavernAnythingElse();
                return;
            }
            Show(shops.WineListText(Female));
            var wines = shops.WineListEntries();
            var choices = wines.Select(w => ($"{w.name} ({w.price})", (Action)(() =>
            {
                Show(shops.FineWine(Female));
                if (Gold < w.price) { ThrownOut(shops.CantPayText(visit)); return; }
                Pay(w.price);
                rounds++;
                bought = true;
                Show(shops.EnjoyText);
                TavernAnythingElse();
            }))).ToList();
            // space: "Well! Our meagre stock is not good enough for thee!"
            choices.Add(("None", () => { Show(shops.MeagreStockText(visit, TownNpc.Hour())); TavernAnythingElse(); }));
            SetButtons(choices.ToArray());
        }

        /// <summary>buy_rumour: "Of what wouldst thou hear my lore?", typed; its price; paid (no surcharge) and who to seek where.</summary>
        static void Rumour()
        {
            var shops = Plugin.U5.Shops;
            var shop = visit;
            Show(shops.LoreQuestion(Female));
            SetButtons();
            typedAnswer = answer =>
            {
                if (string.IsNullOrWhiteSpace(answer)) { TavernAnythingElse(); return; }
                var r = shops.FindRumour(answer.Trim());
                if (r == null) { Show(shops.CannotHelpText); Rumour(); return; }
                Show(shops.RumourPrice(shop, TownNpc.Hour(), r));
                YesNo(() =>
                {
                    if (Gold < r.Price) { ThrownOut(shops.OnlyPayingCustomers(shop, TownNpc.Hour(), Female)); return; }
                    Pay(r.Price, surcharge: false);
                    Show(shops.RumourAnswer(shop, TownNpc.Hour(), r, random));
                    TavernAnythingElse();
                }, () => { Show("No"); TavernAnythingElse(); });
            };
            prompt.text = "You respond";
            field.text = "";
            field.gameObject.SetActive(true);
            Focus();
        }
    }
}
