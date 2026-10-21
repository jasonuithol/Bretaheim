using System;
using System.Collections.Generic;

namespace Bretaheim.Data
{
    /// <summary>Where a spell may be cast (DATA.OVL g_spell_places, one byte per spell; CAST.OVL cast_spell_command).</summary>
    [Flags]
    public enum SpellPlace { None = 0, Combat = 1, Dungeon = 2, Town = 4, Overworld = 8 }

    /// <summary>Why a cast did not come to the spell's effect (cast_spell_command's early words).</summary>
    public enum CastCheck { Ok, NotHere, Absorbed, NoneMixed, ManaTooLow, LevelTooLow }

    /// <summary>One of the 48 spells: its words, circle, recipe and places.</summary>
    public class Spell
    {
        public int Index;
        public string Words;
        /// <summary>The reagents mixed for it: bit 0x80 &gt;&gt; r is reagent r (Sulfur Ash 0 … Mandrake 7).</summary>
        public byte Recipe;
        public SpellPlace Places;

        public int Circle => Magic.CircleOf(Index);
        public int ManaCost => Circle;
        public List<int> Reagents => Magic.ReagentsOf(Recipe);
    }

    /// <summary>
    /// The original's magic (CAST.OVL, CAST2.OVL, CMDS.OVL cmd_mix_reagents; rust/src/ovl/cast*.rs),
    /// kept free of Unity: the spell table read from DATA.OVL, mixing, the cast's checks, the
    /// evasion roll, the potions' surprise, and the turns and minutes the effects last.
    /// </summary>
    public static class Magic
    {
        public const int Count = 48, Reagents = 8, Potions = 8, Scrolls = 8;
        /// <summary>DATA.OVL (DGROUP): the places byte and the recipe byte of each spell.</summary>
        public const int PlacesTable = Dg.SpellUnk1c90, RecipeTable = Dg.SpellUnk1cc0;
        /// <summary>An overworld turn in seconds (the encounter turn, MAPPING_DECISIONS 2026-10-19): the effects' turns are these.</summary>
        public const float TurnSeconds = 8f;
        /// <summary>A party member's status 'P' (protection): +3 to their defence (compute_member_defense).</summary>
        public const int ProtectionDefence = 3;
        /// <summary>The Shadowlords' creature type (tile 0xfc): An Tym is absorbed when one is near.</summary>
        public const int Shadowlord = 0x2f;
        /// <summary>Monsters that sleep, charm, fear and shrink do not touch (is_spell_immune_combatant).</summary>
        public static bool Immune(int type) => type == 0x2f || type == 0x0e || type == 0x0f;

        // spell indices (DATA.OVL order)
        public const int InLor = 0, GravPor = 1, AnZu = 2, AnNox = 3, Mani = 4, AnYlem = 5, AnSanct = 6, AnXenCorp = 7,
            RelHur = 8, InWis = 9, KalXen = 10, InXenMani = 11, VasLor = 12, VasFlam = 13, InFlamGrav = 14, InNoxGrav = 15,
            InZuGrav = 16, InPor = 17, AnGrav = 18, InSanct = 19, InSanctGrav = 20, UusPor = 21, DesPor = 22, WisQuas = 23,
            InBetXen = 24, AnExPor = 25, InExPor = 26, VasMani = 27, InZu = 28, RelTym = 29, InVasPorYlem = 30, QuasAnWis = 31,
            InAn = 32, WisAnYlem = 33, AnXenEx = 34, RelXenBet = 35, SanctLor = 36, XenCorp = 37, InQuasXen = 38, InQuasWis = 39,
            InNoxHur = 40, InQuasCorp = 41, InManiCorp = 42, KalXenCorp = 43, InVasGravCorp = 44, InFlamHur = 45, VasRelPor = 46, AnTym = 47;

        // potions (colour order: Blue, Yellow, Red, Green, Orange, Purple, Black, White)
        public const int Blue = 0, Yellow = 1, Red = 2, Green = 3, Orange = 4, Purple = 5, Black = 6, White = 7;

        public static int CircleOf(int spell) => spell / 6 + 1;

        public static Spell[] Read(DataOvl ovl)
        {
            var all = new Spell[Count];
            for (int s = 0; s < Count; s++)
                all[s] = new Spell { Index = s, Words = ovl.SpellWords(s), Recipe = ovl.U8(RecipeTable + s), Places = (SpellPlace)ovl.U8(PlacesTable + s) };
            return all;
        }

        public static List<int> ReagentsOf(byte mask)
        {
            var r = new List<int>();
            for (int i = 0; i < Reagents; i++) if ((mask & (0x80 >> i)) != 0) r.Add(i);
            return r;
        }

        public static byte MaskOf(IEnumerable<int> reagents)
        {
            int m = 0;
            foreach (int r in reagents) if (r >= 0 && r < Reagents) m |= 0x80 >> r;
            return (byte)m;
        }

        // ── mixing (cmd_mix_reagents) ───────────────────────────────────────────────────────

        /// <summary>
        /// How many of a mixture the reagents allow: each chosen reagent is spent once per
        /// mixture (ask_mix_count: "Insufficient reagents!" past the fewest held); 0 when none is
        /// chosen.
        /// </summary>
        public static int MostToMix(byte mask, Func<int, int> held)
        {
            if (mask == 0) return 0;
            int most = int.MaxValue;
            foreach (int r in ReagentsOf(mask)) most = Math.Min(most, held(r));
            return Math.Max(0, Math.Min(most, 99));
        }

        /// <summary>A mixture of exactly the spell's recipe makes it ("Done!"); any other blows up in the mixer's face (trap_effect).</summary>
        public static bool Mixes(Spell spell, byte mask) => spell != null && mask != 0 && spell.Recipe == mask;

        /// <summary>A spell's mixtures carried are capped at 99 (cmd_mix_reagents).</summary>
        public const int MostMixed = 99;

        // ── casting (cast_spell_command) ────────────────────────────────────────────────────

        /// <summary>
        /// Before the spell: the place (Blackthorn's palace without the crown and Stonegate absorb
        /// all magic), a mixture carried, then the mana (circle) and the level (circle). As the
        /// original, a cast that gets past the mixture spends it, and one that gets past the mana
        /// spends the mana too, even when the level then fails it.
        /// </summary>
        public static CastCheck Check(Spell spell, SpellPlace here, int location, bool wearsCrown, int mixed, int mana, int level)
        {
            if ((location == BlackthornsPalace && !wearsCrown) || location == Stonegate) return CastCheck.Absorbed;
            if ((spell.Places & here) == 0) return CastCheck.NotHere;
            if (mixed <= 0) return CastCheck.NoneMixed;
            if (mana < spell.Circle) return CastCheck.ManaTooLow;
            if (level < spell.Circle) return CastCheck.LevelTooLow;
            return CastCheck.Ok;
        }

        public const int BlackthornsPalace = 0x12, Stonegate = 0x1d, Doom = 0x28;

        /// <summary>The Avatar's most mana: their intelligence (a mage's; OUTSUBS, the inn and the camp all fill it so).</summary>
        public static int MaxMana(Avatar a) => a.Intelligence;

        /// <summary>A rest of six hours or more at a camp, a night at an inn or the apparition fills the mana (cmd_hole_up: hours &gt; 5).</summary>
        public const double RestHours = 6;

        /// <summary>
        /// COMSUBS target_evades for spells: the target evades when a roll of 1..30 is under
        /// (its intelligence − the caster's + 30) / 2 (a monster's "intelligence" is its stats +1).
        /// </summary>
        public static bool Evades(int casterIntelligence, int targetIntelligence, int roll1to30) => roll1to30 < (targetIntelligence - casterIntelligence + 30) / 2;

        /// <summary>A potion is not always what its colour says (use_potion): 1 in 16 is orange (sleep), 1 in 16 any potion.</summary>
        public static int Drunk(int potion, Random rnd)
        {
            int r = rnd.Next(16);
            if (r == 0) return Orange;
            if (r == 1) return rnd.Next(8);
            return potion;
        }

        /// <summary>The attack value of a spell's missile or field (DATA.OVL item attack 0x30..0x36: Grav Por 16, Vas Flam 30, Xen Corp 99; fields 18/0/21/0).</summary>
        public static int MissileWeapon(int spell) => spell == GravPor ? 0x30 : spell == VasFlam ? 0x31 : spell == XenCorp ? 0x32
            : spell == InNoxGrav ? 0x33 : spell == InZuGrav ? 0x34 : spell == InFlamGrav ? 0x35 : spell == InSanctGrav ? 0x36 : -1;

        /// <summary>Kal Xen's animal (cast_kal_xen_summon_animal): from a roll 0..15, a giant rat (6 in 16), a giant spider (5), a bat (3) or a python (2).</summary>
        public static int SummonedAnimal(int roll0to15) => roll0to15 < 6 ? 0x14 : roll0to15 < 11 ? 0x16 : roll0to15 < 14 ? 0x15 : 0x22;

        public const int Daemon = 0x26, InsectSwarm = 0x1f, GiantRat = 0x14;

        /// <summary>A timed party status (start_timed_effect): its turns.</summary>
        public static int StatusTurns(int spell, bool scroll)
        {
            switch (spell)
            {
                case InSanct: return scroll ? 100 : 20;
                case RelTym: return 30;
                case QuasAnWis: return 20;
                case InAn: return scroll ? 20 : 10;
                case AnTym: return scroll ? 20 : 10;
                default: return 0;
            }
        }

        /// <summary>The light spells' game minutes (g_light_spell_turns, counted down by pass_time's minutes): In Lor 100, Vas Lor 255, the scroll 240.</summary>
        public static int LightMinutes(int spell, bool scroll) => scroll ? 240 : spell == VasLor ? 255 : 100;

        /// <summary>Seconds of real time a number of Britannia minutes lasts, for a day of this many seconds.</summary>
        public static float MinutesToSeconds(int minutes, float dayLengthSeconds) => minutes / 1440f * dayLengthSeconds;

        /// <summary>In Wis (in_wis_print_position): the position in the rune letters of the map, "Y'Y", "X'X" (each coordinate as two letters, high nibble first).</summary>
        public static string RunePosition(int x, int y)
        {
            char Hi(int v) => (char)('A' + ((v >> 4) & 15));
            char Lo(int v) => (char)('A' + (v & 15));
            return $"{Hi(y)}'{Lo(y)}\", {Hi(x)}'{Lo(x)}\"";
        }

        /// <summary>
        /// A wind spell's hit on one creature (cast_wind_spell): In Zu sleeps (unless it evades or
        /// is immune), In Nox Hur poisons (unless a 1..30 roll is under its dexterity), In Flam Hur
        /// burns for 0..30, In Vas Grav Corp kills (99; unless it evades or is immune).
        /// </summary>
        public enum WindKind { Sleep = 1, Poison = 2, Fire = 3, Death = 4 }

        public static WindKind? WindOf(int spell) => spell == InZu ? WindKind.Sleep : spell == InNoxHur ? WindKind.Poison
            : spell == InFlamHur ? WindKind.Fire : spell == InVasGravCorp ? WindKind.Death : (WindKind?)null;

        /// <summary>
        /// The earthquake's hit on one enemy (cast_in_vas_por_ylem_earthquake): 1..20 when its
        /// dexterity is no more than a roll of 1..30, else none (0).
        /// </summary>
        public static int Earthquake(int dexterity, Random rnd)
        {
            int roll = rnd.Next(1, 31);
            return dexterity <= roll ? rnd.Next(1, 21) : 0;
        }

        /// <summary>Mani's healing (heal_member): 1..30 hit points.</summary>
        public static int Healing(Random rnd) => rnd.Next(1, 31);

        /// <summary>In Xen Mani's food (cast_in_xen_mani_create_food): 1..3 rations.</summary>
        public static int CreatedFood(Random rnd) => rnd.Next(1, 4);

        /// <summary>
        /// In Mani Corp (in_mani_corp_resurrect): the risen keep their experience only in
        /// proportion to the party's karma (exp × karma / 100, unless karma is 98 or more).
        /// </summary>
        public static int ExperienceAfterResurrection(int experience, int karma) => karma < 98 ? experience * karma / 100 : experience;
    }
}
