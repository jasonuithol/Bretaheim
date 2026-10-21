using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace U5InValheim.Data
{
    public enum ItemKind { Equipment, ArmourPart, Spell, Scroll, Potion, Reagent, Moonstone, Special }

    /// <summary>One Ultima item as a Valheim item: our own copy of a Valheim item (its prefab), or the Valheim item itself.</summary>
    public class ItemDef
    {
        /// <summary>Our item's prefab name ("U5_…"), or the Valheim item's when it is used as it is (torches, gold).</summary>
        public string Prefab;
        /// <summary>The Valheim item it is made from.</summary>
        public string Base;
        /// <summary>Shown name (spells: filled in from DATA.OVL).</summary>
        public string Name;
        public ItemKind Kind;
        /// <summary>The Ultima number within its kind: inventory id 0..111 (equipment, spells, scrolls, potions), reagent 0..7, moonstone 0..7.</summary>
        public int Id = -1;
        /// <summary>"#rrggbb" when recoloured.</summary>
        public string Tint;
        /// <summary>Equipment: our pieces given with it (an armour's leggings); ArmourPart: the armour it belongs to.</summary>
        public string[] Extra = new string[0];
        public string PartOf;
        /// <summary>items.json "effect" and "use" (what wearing or using it will do).</summary>
        public string Effect, Use;
        /// <summary>The spell a scroll holds (spell index 0..47).</summary>
        public int Spell = -1;

        public bool IsOurs => Prefab != Base;
    }

    /// <summary>
    /// assets/mappings/items.json: every Ultima item that can be carried, as a Valheim item. Each is
    /// our own copy of a Valheim item ("U5_" + its name) so that it has the Ultima name and numbers,
    /// and so two Ultima items made from one Valheim item (the pocket watch and the black badge) stay apart.
    /// </summary>
    public class ItemMap
    {
        public const int SpellBase = 48, ScrollBase = 96, PotionBase = 104;
        public readonly List<ItemDef> All = new List<ItemDef>();
        readonly Dictionary<string, ItemDef> byPrefab = new Dictionary<string, ItemDef>();
        readonly Dictionary<int, ItemDef> equipment = new Dictionary<int, ItemDef>();
        readonly Dictionary<int, ItemDef> reagents = new Dictionary<int, ItemDef>();
        readonly Dictionary<string, ItemDef> special = new Dictionary<string, ItemDef>();
        readonly Dictionary<int, ItemDef> inventory = new Dictionary<int, ItemDef>();

        /// <summary>Our prefab name for an Ultima name: "U5_" + its letters and digits ("Amulet of Turning" → U5_AmuletofTurning).</summary>
        public static string PrefabFor(string name)
        {
            var sb = new StringBuilder("U5_");
            foreach (char c in name) if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        public static ItemMap Parse(string json)
        {
            var m = new ItemMap();
            var root = Json.Obj(Json.Parse(json));
            foreach (var e in Json.Arr(root["equipment"]))
            {
                var o = Json.Obj(e);
                string base_ = Json.Str(o, "prefab"), name = Json.Str(o, "u5");
                if (base_ == null) continue;
                var def = new ItemDef { Prefab = PrefabFor(name), Base = base_, Name = name, Kind = ItemKind.Equipment, Id = (int)Json.Num(o, "id"),
                                        Tint = Json.Str(o, "tint"), Effect = Json.Str(o, "effect"), Use = Json.Str(o, "use") };
                var parts = new List<string>();
                foreach (var piece in o.TryGetValue("extra", out var x) ? Json.Arr(x).ConvertAll(v => (string)v) : new List<string>())
                {
                    // an armour's leggings: "Ring Mail Leggings", worn with it, counted with it
                    var part = new ItemDef { Prefab = def.Prefab + "_Legs", Base = piece, Name = name + " Leggings", Kind = ItemKind.ArmourPart, Id = def.Id, PartOf = def.Prefab, Tint = def.Tint };
                    m.Add(part);
                    parts.Add(part.Prefab);
                }
                def.Extra = parts.ToArray();
                m.Add(def);
                m.equipment[def.Id] = def;
                m.inventory[def.Id] = def;
            }
            foreach (var e in Json.Arr(root["reagents"]))
            {
                var o = Json.Obj(e);
                if (Json.Str(o, "prefab") == null) continue;
                var def = new ItemDef { Prefab = PrefabFor(Json.Str(o, "u5")), Base = Json.Str(o, "prefab"), Name = Json.Str(o, "u5"), Kind = ItemKind.Reagent, Id = (int)Json.Num(o, "id"), Tint = Json.Str(o, "tint") };
                m.Add(def);
                m.reagents[def.Id] = def;
            }
            // mixed spells: one mead base per circle (six spells each); their names come from DATA.OVL
            var spells = Json.Obj(root["spells"]);
            var byCircle = Json.Arr(spells["prefab_by_circle"]).ConvertAll(v => (string)v);
            for (int s = 0; s < 48; s++)
            {
                var def = new ItemDef { Prefab = $"U5_Spell{s:00}", Base = byCircle[s / 6], Kind = ItemKind.Spell, Id = SpellBase + s, Spell = s };
                m.Add(def);
                m.inventory[def.Id] = def;
            }
            // scrolls: the spell each holds (spell index); named from DATA.OVL
            var scrolls = Json.Obj(root["scrolls"]);
            var held = Json.Arr(scrolls["spells"]);
            for (int i = 0; i < held.Count; i++)
            {
                var def = new ItemDef { Prefab = $"U5_Scroll{i}", Base = Json.Str(scrolls, "prefab"), Kind = ItemKind.Scroll, Id = ScrollBase + i,
                                        Spell = (int)(double)held[i], Tint = Json.Str(scrolls, "tint") };
                m.Add(def);
                m.inventory[def.Id] = def;
            }
            var potions = Json.Obj(root["potions"]);
            foreach (var e in Json.Arr(potions["colours"]))
            {
                var o = Json.Obj(e);
                string colour = Json.Str(o, "u5");
                var def = new ItemDef { Prefab = PrefabFor(colour + " Potion"), Base = Json.Str(potions, "prefab"), Name = colour + " Potion", Kind = ItemKind.Potion, Id = (int)Json.Num(o, "id"), Tint = Json.Str(o, "tint") };
                m.Add(def);
                m.inventory[def.Id] = def;
            }
            if (root.TryGetValue("moonstones", out var ms))
            {
                var o = Json.Obj(ms);
                var phases = Json.Arr(o["phases"]);
                for (int i = 0; i < phases.Count; i++)
                    m.Add(new ItemDef { Prefab = $"U5_Moonstone{i}", Base = Json.Str(o, "prefab"), Name = $"Moonstone ({phases[i]})", Kind = ItemKind.Moonstone, Id = i, Tint = Json.Str(o, "tint") });
            }
            foreach (var e in Json.Arr(root["special"]))
            {
                var o = Json.Obj(e);
                string base_ = Json.Str(o, "prefab"), name = Json.Str(o, "u5");
                if (base_ == null) continue;
                // used as Valheim has it (torches, gold) or our own copy
                var def = new ItemDef { Prefab = Json.Bool(o, "as_is") ? base_ : PrefabFor(name), Base = base_, Name = name, Kind = ItemKind.Special,
                                        Tint = Json.Str(o, "tint"), Effect = Json.Str(o, "effect"), Use = Json.Str(o, "use") };
                m.Add(def);
                m.special[name] = def;
            }
            return m;
        }

        /// <summary>Names the spells and scrolls by their words of power (DATA.OVL).</summary>
        public ItemMap Named(DataOvl ovl)
        {
            foreach (var d in All)
                if (d.Kind == ItemKind.Spell) d.Name = ovl.SpellWords(d.Spell);
                else if (d.Kind == ItemKind.Scroll) d.Name = "Scroll of " + ovl.SpellWords(d.Spell);
            return this;
        }

        void Add(ItemDef def)
        {
            All.Add(def);
            byPrefab[def.Prefab] = def;
        }

        /// <summary>Our item by its prefab name, or null.</summary>
        public ItemDef Get(string prefab) => prefab != null && byPrefab.TryGetValue(prefab, out var d) ? d : null;

        /// <summary>A carried item by its Ultima inventory id 0..111.</summary>
        public ItemDef Inventory(int id) => inventory.TryGetValue(id, out var d) ? d : null;
        public ItemDef Reagent(int id) => reagents.TryGetValue(id, out var d) ? d : null;
        public ItemDef Special(string name) => special.TryGetValue(name, out var d) ? d : null;

        /// <summary>
        /// The Ultima equipment id a Valheim item stands for, or −1: our item (an armour's leggings
        /// count as nothing, the armour itself carries its defence), or the plain Valheim item an
        /// older shop sold (its base).
        /// </summary>
        public int EquipmentOf(string prefab)
        {
            var d = Get(prefab);
            if (d != null) return d.Kind == ItemKind.Equipment ? d.Id : -1;
            foreach (var kv in equipment) if (kv.Value.Base == prefab) return kv.Key;
            return -1;
        }

        static readonly string[] Shards = { "Shard of Falsehood", "Shard of Hatred", "Shard of Cowardice" };

        /// <summary>The Valheim items (prefab, count) some loot is: an armour comes with its leggings; nothing for what is not an item.</summary>
        public List<(string prefab, int count)> Prefabs(Loot loot)
        {
            var r = new List<(string, int)>();
            ItemDef d;
            switch (loot.Kind)
            {
                case LootKind.Item: d = Inventory(loot.Value); break;
                case LootKind.Potion: d = Inventory(PotionBase + (loot.Value & 7)); break;
                case LootKind.Scroll: d = Inventory(ScrollBase + (loot.Value & 7)); break;
                case LootKind.Moonstone: d = Get($"U5_Moonstone{loot.Value & 7}"); break;
                case LootKind.Shard: d = Special(Shards[loot.Value % 3]); break;
                case LootKind.Gold: d = Special("Gold"); break;
                case LootKind.Food: d = Special("Food"); break;
                case LootKind.Keys: d = Special("Key"); break;
                case LootKind.SkullKeys: d = Special("Skull Key"); break;
                case LootKind.Gems: d = Special("Gem"); break;
                case LootKind.Torches: d = Special("Torch"); break;
                case LootKind.HmsCapePlans: d = Special("HMS Cape Plans"); break;
                case LootKind.SandalwoodBox: d = Special("Sandalwood Box"); break;
                case LootKind.Carpet: d = Special("Magic Carpet"); break;
                case LootKind.Crown: d = Special("Crown of Lord British"); break;
                case LootKind.Sceptre: d = Special("Sceptre of Lord British"); break;
                case LootKind.AmuletOfLordBritish: d = Special("Amulet of Lord British"); break;
                case LootKind.Grapple: d = Special("Grapple"); break;
                case LootKind.Sextant: d = Special("Sextant"); break;
                case LootKind.Spyglass: d = Special("Spyglass"); break;
                case LootKind.BlackBadge: d = Special("Black Badge"); break;
                default: d = null; break;
            }
            if (d == null || loot.Count <= 0) return r;
            r.Add((d.Prefab, loot.Count));
            foreach (var x in d.Extra) r.Add((x, loot.Count));
            return r;
        }

        /// <summary>Our prefab of a shop ware (<see cref="Ware.Item"/>) and the pieces given with it, or null when it has none.</summary>
        public (string prefab, string[] extra)? Of(int item)
        {
            ItemDef d;
            if (item >= Shops.ReagentBase && item < Shops.ReagentBase + 8) d = Reagent(item - Shops.ReagentBase);
            else if (item == Shops.Torch) d = Special("Torch");
            else if (item == Shops.Gem) d = Special("Gem");
            else if (item == Shops.Food) d = Special("Food");
            else d = equipment.TryGetValue(item, out var e) ? e : null;
            return d == null ? ((string, string[])?)null : (d.Prefab, d.Extra);
        }
    }
}
