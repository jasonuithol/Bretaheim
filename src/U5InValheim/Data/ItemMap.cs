using System.Collections.Generic;

namespace U5InValheim.Data
{
    /// <summary>assets/mappings/items.json: what each Ultima item is in Valheim (the prefab, and any extra pieces given with it).</summary>
    public class ItemMap
    {
        readonly Dictionary<int, (string prefab, string[] extra)> equipment = new Dictionary<int, (string, string[])>();
        readonly Dictionary<int, string> reagents = new Dictionary<int, string>();
        readonly Dictionary<string, string> special = new Dictionary<string, string>();

        public static ItemMap Parse(string json)
        {
            var m = new ItemMap();
            var root = Json.Obj(Json.Parse(json));
            foreach (var e in Json.Arr(root["equipment"]))
            {
                var o = Json.Obj(e);
                var extra = o.TryGetValue("extra", out var x) ? Json.Arr(x).ConvertAll(v => (string)v).ToArray() : new string[0];
                if (Json.Str(o, "prefab") != null) m.equipment[(int)Json.Num(o, "id")] = (Json.Str(o, "prefab"), extra);
            }
            foreach (var e in Json.Arr(root["reagents"]))
            {
                var o = Json.Obj(e);
                if (Json.Str(o, "prefab") != null) m.reagents[(int)Json.Num(o, "id")] = Json.Str(o, "prefab");
            }
            foreach (var e in Json.Arr(root["special"]))
            {
                var o = Json.Obj(e);
                if (Json.Str(o, "prefab") != null) m.special[Json.Str(o, "u5")] = Json.Str(o, "prefab");
            }
            return m;
        }

        /// <summary>The Valheim prefab of a shop ware (<see cref="Ware.Item"/>) and the pieces given with it, or null when it has none.</summary>
        public (string prefab, string[] extra)? Of(int item)
        {
            if (item >= Shops.ReagentBase && item < Shops.ReagentBase + 8)
                return reagents.TryGetValue(item - Shops.ReagentBase, out var r) ? (r, new string[0]) : ((string, string[])?)null;
            string special = item == Shops.Torch ? "Torch" : item == Shops.Gem ? "Gem" : item == Shops.Food ? "Food" : null;
            if (special != null) return this.special.TryGetValue(special, out var s) ? (s, new string[0]) : ((string, string[])?)null;
            return equipment.TryGetValue(item, out var e) ? e : ((string, string[])?)null;
        }
    }
}
