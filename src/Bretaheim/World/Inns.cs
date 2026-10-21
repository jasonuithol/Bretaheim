using System.Globalization;
using System.Linq;
using Bretaheim.Data;
using Bretaheim.WorldGen.Core;
using UnityEngine;

namespace Bretaheim.WorldGen
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

        /// <summary>
        /// The time (world seconds) the night paid for ends: the first 8 o'clock at least six hours
        /// off, so a room taken at any hour covers the coming night (taken before dawn: tonight too).
        /// </summary>
        static double NextMorning()
        {
            double now = ZNet.instance.GetTimeSeconds(), day = EnvMan.instance.m_dayLengthSec;
            double eight = 0.15 + (8 / 24.0 - 0.25) / 0.5 * 0.7; // (as Crime: Valheim's clock runs faster at night)
            double end = System.Math.Floor(now / day) * day + eight * day;
            while (end < now + day * 6 / 24.0) end += day;
            return end;
        }

        /// <summary>This player may sleep in this town bed: a room paid for tonight at this settlement's inn, the bed one of its beds.</summary>
        public static bool MayRest(Player p, Bed bed) => MayRest(p, bed, out _);

        /// <summary>As MayRest, with why not (for the log), or null when there is no room paid for.</summary>
        public static bool MayRest(Player p, Bed bed, out string why)
        {
            why = null;
            if (p == null || bed == null || !p.m_customData.TryGetValue(InnKey, out var v)) return false;
            var parts = v.Split('|');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int loc) || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double until)) { why = $"record \"{v}\" unreadable"; return false; }
            double now = ZNet.instance.GetTimeSeconds();
            if (now > until) { why = $"the night paid for ended (at {until:0}, now {now:0})"; p.m_customData.Remove(InnKey); return false; }
            var town = Settlements.Get(loc);
            if (town == null) { why = $"settlement {loc} not built"; return false; }
            // one of the inn's beds: in its rooms, on their storey
            var l = bed.transform.position - town.Origin;
            int cell = town.Plan.CellAt(l.x, l.z);
            foreach (var kv in town.Plan.InnRooms)
                if (cell >= 0 && kv.Value[cell] && Mathf.Abs(l.y - town.Plan.LevelY(kv.Key)) < 2f) return true;
            why = $"the bed at {l} (tile {cell % SettlementPlan.Size},{cell / SettlementPlan.Size}) is not one of {town.Location.Name}'s inn's (rooms on storeys {string.Join(",", town.Plan.InnRooms.Keys)})";
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
