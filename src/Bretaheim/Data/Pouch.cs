using System;
using System.Collections.Generic;
using System.Globalization;

namespace Bretaheim.Data
{
    /// <summary>
    /// The Avatar's reagents and mixed spells, kept out of Valheim's inventory (MAPPING_DECISIONS
    /// 2026-10-22: "spell ingredients and spells, which have separate menus"): a count of each of
    /// the eight reagents and the 48 spells, at most 99 each (the original's caps), and the spell
    /// readied for the cast key, all in the character's custom data.
    /// </summary>
    public class Pouch
    {
        public const string ReagentKey = "u5_pouch_reagent_", SpellKey = "u5_pouch_spell_", ReadiedKey = "u5_readied_spell";
        /// <summary>The most of one reagent or one spell carried (the original's: cmd_mix_reagents, the reagent shops).</summary>
        public const int Most = Magic.MostMixed;

        public readonly int[] Reagents = new int[Magic.Reagents];
        public readonly int[] Spells = new int[Magic.Count];
        /// <summary>The spell the cast key casts (0..47), or −1.</summary>
        public int Readied = -1;

        /// <summary>The items the pouch holds instead of the inventory: the reagents and the mixed spells.</summary>
        public static bool Holds(ItemDef d) => d != null && (d.Kind == ItemKind.Reagent || d.Kind == ItemKind.Spell);

        public static Pouch Load(IDictionary<string, string> data)
        {
            var p = new Pouch();
            if (data == null) return p;
            for (int r = 0; r < p.Reagents.Length; r++) p.Reagents[r] = Read(data, ReagentKey + r, 0);
            for (int s = 0; s < p.Spells.Length; s++) p.Spells[s] = Read(data, SpellKey + s.ToString("00"), 0);
            p.Readied = Read(data, ReadiedKey, -1);
            if (p.Readied >= Magic.Count) p.Readied = -1;
            return p;
        }

        static int Read(IDictionary<string, string> data, string key, int none)
            => data.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? Math.Max(none, Math.Min(Most, n)) : none;

        /// <summary>Writes the counts (an empty count leaves no key).</summary>
        public void Save(IDictionary<string, string> data)
        {
            for (int r = 0; r < Reagents.Length; r++) Write(data, ReagentKey + r, Reagents[r]);
            for (int s = 0; s < Spells.Length; s++) Write(data, SpellKey + s.ToString("00"), Spells[s]);
            if (Readied >= 0) data[ReadiedKey] = Readied.ToString(CultureInfo.InvariantCulture);
            else data.Remove(ReadiedKey);
        }

        static void Write(IDictionary<string, string> data, string key, int n)
        {
            if (n > 0) data[key] = n.ToString(CultureInfo.InvariantCulture);
            else data.Remove(key);
        }

        int[] Table(ItemDef d) => d == null ? null : d.Kind == ItemKind.Reagent ? Reagents : d.Kind == ItemKind.Spell ? Spells : null;
        static int Index(ItemDef d) => d.Kind == ItemKind.Spell ? d.Spell : d.Id;

        /// <summary>How many of an item the pouch holds (0 for what it does not hold).</summary>
        public int Count(ItemDef d)
        {
            var t = Table(d);
            int i = t == null ? -1 : Index(d);
            return i >= 0 && i < t.Length ? t[i] : 0;
        }

        /// <summary>
        /// Puts up to <paramref name="n"/> of an item in the pouch, as many as its cap leaves room
        /// for; returns how many went in (what is over the cap is lost, as the original's).
        /// </summary>
        public int Add(ItemDef d, int n)
        {
            var t = Table(d);
            int i = t == null ? -1 : Index(d);
            if (i < 0 || i >= t.Length || n <= 0) return 0;
            int taken = Math.Min(n, Most - t[i]);
            if (taken <= 0) return 0;
            t[i] += taken;
            return taken;
        }

        /// <summary>Takes <paramref name="n"/> of an item; false (nothing taken) when there are fewer.</summary>
        public bool Take(ItemDef d, int n)
        {
            var t = Table(d);
            int i = t == null ? -1 : Index(d);
            if (i < 0 || i >= t.Length || n < 0 || t[i] < n) return false;
            t[i] -= n;
            return true;
        }

        public bool IsFull(ItemDef d) => Table(d) != null && Count(d) >= Most;

        public int SpellCount(int spell) => spell >= 0 && spell < Spells.Length ? Spells[spell] : 0;

        /// <summary>Adds mixed spells (capped); returns how many went in.</summary>
        public int AddSpell(int spell, int n)
        {
            if (spell < 0 || spell >= Spells.Length || n <= 0) return 0;
            int taken = Math.Min(n, Most - Spells[spell]);
            if (taken <= 0) return 0;
            Spells[spell] += taken;
            return taken;
        }

        public bool TakeSpell(int spell, int n = 1)
        {
            if (spell < 0 || spell >= Spells.Length || Spells[spell] < n) return false;
            Spells[spell] -= n;
            return true;
        }

        public bool TakeReagent(int reagent, int n)
        {
            if (reagent < 0 || reagent >= Reagents.Length || Reagents[reagent] < n) return false;
            Reagents[reagent] -= n;
            return true;
        }

        /// <summary>The spells held, in the original's order (circle by circle).</summary>
        public IEnumerable<int> HeldSpells()
        {
            for (int s = 0; s < Spells.Length; s++) if (Spells[s] > 0) yield return s;
        }

        /// <summary>
        /// An older character's reagents and spells (inventory items) moved into the pouch: each
        /// (prefab, count) the pouch holds is taken (up to the caps); returns those moved, with
        /// how many went in.
        /// </summary>
        public List<(string prefab, int count, int taken)> Absorb(ItemMap map, IEnumerable<(string prefab, int count)> carried)
        {
            var moved = new List<(string, int, int)>();
            foreach (var (prefab, count) in carried)
            {
                var d = map.Get(prefab);
                if (!Holds(d)) continue;
                moved.Add((prefab, count, Add(d, count)));
            }
            return moved;
        }

        public override string ToString()
        {
            var parts = new List<string>();
            for (int r = 0; r < Reagents.Length; r++) parts.Add($"r{r}={Reagents[r]}");
            foreach (int s in HeldSpells()) parts.Add($"s{s}={Spells[s]}");
            parts.Add($"readied={Readied}");
            return string.Join(" ", parts);
        }
    }
}
