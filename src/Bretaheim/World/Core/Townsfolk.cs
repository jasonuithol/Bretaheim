using System.Collections.Generic;
using Bretaheim.Data;

namespace Bretaheim.WorldGen.Core
{
    /// <summary>How one kind of townsperson looks (npcs.json "people").</summary>
    public class PersonLook
    {
        public int Type;
        public string U5;
        /// <summary>"human" (a dressed Valheim body), or a prefab name.</summary>
        public string Base = "human";
        public string[] Outfit = new string[0];
        public float Scale = 1f;
    }

    /// <summary>The people of the settlements (.NPC files) as the game shows them.</summary>
    public static class Townsfolk
    {
        static readonly string[] Trades = { "Armourer", "Barkeep", "Ostler", "Shipwright", "Herbalist", "Guildmaster", "Healer", "Innkeeper" };

        /// <summary>What hovering over a townsperson shows: their name, else their trade or kind.</summary>
        public static string Label(U5Data data, Data.Location loc, SettlementNpc npc)
        {
            var name = data.NpcName(loc, npc);
            if (!string.IsNullOrEmpty(name)) return name;
            if (npc.IsShopkeeper && (int)npc.Trade < Trades.Length) return Trades[(int)npc.Trade];
            if (npc.IsGuard || npc.CreatureType == 12) return "Guard";
            switch (npc.CreatureType)
            {
                case 0: return "Mage";
                case 1: case 7: return "Bard";
                case 2: return "Fighter";
                case 5: return "Merchant";
                case 6: return "Jester";
                case 8: return "Pirate";
                case 10: return "Child";
                case 11: return "Beggar";
                case 14: return "Blackthorn";
                case 15: return "Lord British";
                default: return npc.CreatureType >= 0 ? "Villager" : "Animal";
            }
        }

        /// <summary>npcs.json: the look of each creature type.</summary>
        public static Dictionary<int, PersonLook> ParseLooks(string json)
        {
            var r = new Dictionary<int, PersonLook>();
            foreach (var item in Json.Arr(Json.Obj(Json.Parse(json))["people"]))
            {
                var o = Json.Obj(item);
                var look = new PersonLook
                {
                    Type = (int)Json.Num(o, "type"), U5 = Json.Str(o, "u5"), Base = Json.Str(o, "base") ?? "human",
                    Scale = (float)Json.Num(o, "scale", 1),
                };
                if (o.TryGetValue("outfit", out var outfit)) look.Outfit = Json.Arr(outfit).ConvertAll(x => (string)x).ToArray();
                r[look.Type] = look;
            }
            return r;
        }
    }
}
