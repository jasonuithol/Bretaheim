using System.Globalization;
using System.Linq;
using U5InValheim.Data;
using U5InValheim.WorldGen.Core;
using UnityEngine;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// Inns (SHOPPES3 inn_shop, Rest): each player pays the innkeeper for a night (the original's
    /// rate for one, discounted for intelligence) and may then sleep in the inn's beds (those of
    /// its rooms, SettlementPlan.InnRooms) until the next morning. Kept on the player ("u5_inn":
    /// settlement and the time the night ends).
    /// </summary>
    public static class Inns
    {
        const string InnKey = "u5_inn";

        /// <summary>The player has paid for tonight in this settlement.</summary>
        public static void Rent(Player p, ShopInfo s)
        {
            if (p == null || ZNet.instance == null || EnvMan.instance == null) return;
            p.m_customData[InnKey] = s.Location.Number + "|" + NextMorning().ToString(CultureInfo.InvariantCulture);
            Plugin.Log.LogInfo($"{p.GetPlayerName()} takes a room at {s.Name} ({s.Location.Name})");
        }

        /// <summary>The time (world seconds) the night paid for ends: 8 o'clock next morning (this morning before 8).</summary>
        static double NextMorning()
        {
            double now = ZNet.instance.GetTimeSeconds(), day = EnvMan.instance.m_dayLengthSec;
            double eight = 0.15 + (8 / 24.0 - 0.25) / 0.5 * 0.7; // (as Crime: Valheim's clock runs faster at night)
            double start = System.Math.Floor(now / day) * day;
            return TownNpc.Hour() < 8 ? start + eight * day : start + day + eight * day;
        }

        /// <summary>This player may sleep in this town bed: a room paid for tonight at this settlement's inn, the bed one of its beds.</summary>
        public static bool MayRest(Player p, Bed bed)
        {
            if (p == null || bed == null || !p.m_customData.TryGetValue(InnKey, out var v)) return false;
            var parts = v.Split('|');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int loc) || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double until)) return false;
            if (ZNet.instance.GetTimeSeconds() > until) { p.m_customData.Remove(InnKey); return false; }
            var town = Settlements.Get(loc);
            if (town == null) return false;
            // one of the inn's beds: in its rooms, on their storey
            var l = bed.transform.position - town.Origin;
            int cell = town.Plan.CellAt(l.x, l.z);
            foreach (var kv in town.Plan.InnRooms)
                if (cell >= 0 && kv.Value[cell] && Mathf.Abs(l.y - town.Plan.LevelY(kv.Key)) < 2f) return true;
            return false;
        }

        /// <summary>The settlement's inn, or null.</summary>
        public static ShopInfo InnOf(Settlements.Built town)
            => Plugin.U5.NpcsOf(town.Location).Where(n => n.Exists && n.IsShopkeeper && n.Trade == ShopTrade.Inn)
                .Select(n => Plugin.U5.Shops.Of(town.Location, n)).FirstOrDefault();

        /// <summary>Where the inn's own bed is (world), on whichever storey has a bed at its cell.</summary>
        public static Vector3? InnBedAt(Settlements.Built town, ShopInfo inn)
        {
            var (x, y) = Plugin.U5.Shops.InnBed(inn);
            int cell = y * SettlementPlan.Size + x;
            foreach (var kv in town.Plan.Seats)
                if (kv.Key.cell == cell && kv.Value.Bed) return town.Origin + new Vector3(kv.Value.X, kv.Value.Y, kv.Value.Z);
            return null;
        }
    }
}
