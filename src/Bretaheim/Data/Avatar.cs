using System;
using System.Collections.Generic;

namespace Bretaheim.Data
{
    /// <summary>
    /// A player's Ultima character (party record 0): strength, dexterity, intelligence, mana,
    /// level and experience; hit points are 30 a level. Kept in the Valheim character's custom
    /// data (one per character, MASTER_PLAN §5.9). A new character's Avatar is not made yet: all
    /// of strength, dexterity, intelligence and mana are 0 until the gypsy's virtue questions in
    /// Iolo's hut make it (§5.13, <see cref="VirtueCasting"/>; INIT.GAM's 15s plus the cards).
    /// Characters from before the questions keep what they have.
    /// </summary>
    public class Avatar
    {
        public const int StartingStat = 15, MaxStat = 30, HitPointsPerLevel = 30, MaxLevel = 8;
        const string Prefix = "u5_avatar_";

        public int Strength = StartingStat, Dexterity = StartingStat, Intelligence = StartingStat;
        public int Mana, Level = 1, Experience;
        /// <summary>Made by the virtue questions (or kept from before them); false: every stat 0 until then.</summary>
        public bool Made = true;

        public int MaxHitPoints => HitPointsPerLevel * Level;

        public static Avatar Load(IDictionary<string, string> data)
        {
            int Get(string key, int fallback) => data != null && data.TryGetValue(Prefix + key, out var v) && int.TryParse(v, out int n) ? n : fallback;
            // (saved before the questions existed: made)
            bool made = data != null && (data.TryGetValue(Prefix + "made", out var m) ? m == "1" : data.ContainsKey(Prefix + "str"));
            int start = made ? StartingStat : 0;
            var a = new Avatar
            {
                Strength = Get("str", start), Dexterity = Get("dex", start), Intelligence = Get("int", start),
                Level = Get("level", 1), Experience = Get("exp", 0), Made = made,
            };
            // a new Avatar starts with full mana (their intelligence)
            a.Mana = Get("mp", a.Intelligence);
            return a;
        }

        public void Save(IDictionary<string, string> data)
        {
            data[Prefix + "str"] = Strength.ToString();
            data[Prefix + "dex"] = Dexterity.ToString();
            data[Prefix + "int"] = Intelligence.ToString();
            data[Prefix + "mp"] = Mana.ToString();
            data[Prefix + "level"] = Level.ToString();
            data[Prefix + "exp"] = Experience.ToString();
            data[Prefix + "made"] = Made ? "1" : "0";
        }

        /// <summary>The level experience earns (OUTSUBS apparition_level_up): 1 + the halvings of experience / 100 (2 at 100, 3 at 200, 4 at 400 …).</summary>
        public static int LevelFor(int experience)
        {
            int level = 1;
            for (int e = experience / 100; e > 0; e >>= 1) level++;
            return level;
        }

        /// <summary>Experience for a kill (COMBAT.OVL apply_damage): a quarter of the creature type's hit points, plus one.</summary>
        public static int ExperienceFor(CreatureStats victim) => (victim.HitPoints >> 2) + 1;

        /// <summary>
        /// The apparition's visit (apparition_level_up): when experience has earned a new level it
        /// is taken, with full hit points, and strength, dexterity or intelligence (at random) rises
        /// by one up to 30. Returns which (0 strength, 1 dexterity, 2 intelligence), or −1 for no
        /// new level.
        /// </summary>
        public int LevelUp(Random rnd)
        {
            int level = LevelFor(Experience);
            if (level == Level) return -1;
            Level = level;
            int which = rnd.Next(0, 3);
            if (which == 0) Strength = Math.Min(MaxStat, Strength + 1);
            else if (which == 1) Dexterity = Math.Min(MaxStat, Dexterity + 1);
            else Intelligence = Math.Min(MaxStat, Intelligence + 1);
            return which;
        }

        /// <summary>
        /// The Avatar's armour (party record +0x18, which COMBAT.OVL reads): the defence of
        /// everything worn, by the equipment ids of the items the player wears. (The original's
        /// compute_member_defense, which would add the same up, always returns 0 through a bad
        /// comparison; no caller uses it.)
        /// </summary>
        public static int Defence(DataOvl ovl, IEnumerable<int> worn)
        {
            int sum = 0;
            foreach (int id in worn) if (id >= 0 && id < 0x2f) sum += ovl.ItemDefense(id);
            return sum;
        }

        /// <summary>The apparition's words (DATA.OVL): "Hail, NAME! For thy valiant deeds, I shall reward thee! Thou art now level N, and stronger!"</summary>
        public static string LevelUpWords(DataOvl ovl, string name, int level, int which)
        {
            const int Hail = 0x776a, Deeds = 0x7774, NowLevel = 0x77a4, And = 0x77b8, Stronger = 0x77c0, Quicker = 0x77ca, Wiser = 0x77d4;
            string s = ovl.CStr(Hail) + name + ovl.CStr(Deeds) + ovl.CStr(NowLevel) + level + ovl.CStr(And) + ovl.CStr(which == 0 ? Stronger : which == 1 ? Quicker : Wiser) + "\"";
            return s.Replace("\n", " ").Trim().Replace("  ", " ");
        }
    }
}
