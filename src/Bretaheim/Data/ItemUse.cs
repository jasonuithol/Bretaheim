using System;
using System.Collections.Generic;

namespace Bretaheim.Data
{
    /// <summary>
    /// The original's rules for using the quest and utility items (CAST.OVL use_item_command, the
    /// View command, LOOKOBJ look_telescope, CAST2 in_wis_print_position), for Jimmy (SJOG
    /// cmd_jimmy) and for the worn rings (rest.rs regeneration_ring_tick, init_combat_party,
    /// find_nearest_enemy): what each answers, from DATA.OVL's own words, and the rolls.
    /// </summary>
    public static class ItemUse
    {
        // DATA.OVL (DGROUP) strings
        public const int SSextant = 0x49fc, SPosition = 0x4a26, SOnlyAtNight = 0x4a16, SOnlyOutdoors = 0x4a06;
        public const int SWatch = 0x4a30, SPm = 0x4a4f, SAm = 0x4a55;
        public const int SSpyglass = 0x498d, SLooking = 0x4998, SNoStars = 0x49a4, SNotHere = 0x49af, SNightSky = 0x72fa;
        public const int SBadge = 0x4a5b, SBadgeWorn = 0x4a63, SRemoved = 0x4895;
        public const int SAmulet = 0x4914, SWearingAmulet = 0x491d, SCrown = 0x4930, SDonCrown = 0x4938, SOfLordBritish = 0x4a84;
        public const int SSceptre = 0x4950, SWieldingSceptre = 0x495a, SFieldDissolved = 0x496f, SNoEffect = 0x4981;
        public const int SCarpet = 0x48bf, SBoarded = 0x48c8, SXitShipFirst = 0x48d2, SOnlyOnFoot = 0x48e4, SCarpetNotHere = 0x48f3;
        public const int SSkullKey = 0x48fe, SFailed = 0x4a7b;
        public const int SViewGem = 0xa258, SNoGems = 0xa266;
        public const int SNoKeys = 0x8ad0, SKeyBroke = 0x8ada, SUnlocked = 0x8ae6;
        public const int SRingVanished = 0xa422;
        public const int SGivePassword = 0x90fc, SPassFriend = 0x913a, Password = 0x4a9a;
        /// <summary>Blackthorn's palace: its guards challenge the party (TALK guard_challenge).</summary>
        public const int BlackthornsPalace = 18;

        /// <summary>Night for the spyglass and the sextant: before 6 or after 18 o'clock.</summary>
        public static bool Night(int hour) => hour < 6 || hour > 18;

        /// <summary>The telescope's night sky (look_telescope) is shown before 6 and from 18 o'clock.</summary>
        public static bool TelescopeNight(int hour) => hour < 6 || hour > 17;

        /// <summary>
        /// The sextant's position (in_wis_print_position): latitude from the row, longitude from the
        /// column, each as two letters A..P (the high and low four bits of the 0..255 coordinate),
        /// as A'B", C'D". The original prints them in its runic font.
        /// </summary>
        public static string Position(int x, int y)
        {
            x &= 0xff;
            y &= 0xff;
            return $"{(char)('A' + (y >> 4))}'{(char)('A' + (y & 15))}\", {(char)('A' + (x >> 4))}'{(char)('A' + (x & 15))}\"";
        }

        /// <summary>Using the sextant: on the Britannia surface outside any settlement and at night, the position; else the original's refusal.</summary>
        public static string Sextant(DataOvl ovl, bool outdoors, int hour, int x, int y)
        {
            if (!outdoors) return ovl.CStr(SOnlyOutdoors).Trim();
            if (!Night(hour)) return ovl.CStr(SOnlyAtNight).Trim();
            return ovl.CStr(SPosition).Trim() + " " + Position(x, y);
        }

        /// <summary>"The pocket watch reads h:mm AM." (12-hour clock, the hour space-padded, minutes in two digits).</summary>
        public static string Watch(DataOvl ovl, int hour, int minute)
        {
            int h = hour % 12;
            if (h == 0) h = 12;
            string reads = ovl.CStr(SWatch);
            reads = reads.Substring(reads.LastIndexOf('\n') + 1);
            return (reads + h + ":" + minute.ToString("00") + ovl.CStr(hour < 12 ? SAm : SPm)).Trim();
        }

        /// <summary>The Britannian hour and minute of a Valheim day fraction (0 = midnight).</summary>
        public static (int hour, int minute) Clock(double dayFraction)
        {
            dayFraction -= Math.Floor(dayFraction);
            int minutes = (int)Math.Floor(dayFraction * 24 * 60) % (24 * 60);
            return (minutes / 60, minutes % 60);
        }

        /// <summary>Jimmying a lock (cmd_jimmy): a roll of 0..29 below the dexterity opens it, else the key breaks.</summary>
        public static bool JimmyOpens(int dexterity, int roll) => roll < dexterity;
        public const int JimmyRoll = 30;
        public static double JimmyChance(int dexterity) => Math.Max(0, Math.Min(JimmyRoll, dexterity)) / (double)JimmyRoll;

        /// <summary>The Ring of Regeneration (regeneration_ring_tick): each turn a roll of 0..7 that comes up 7 heals 1 hit point.</summary>
        public static bool Regenerates(int roll) => roll == 7;
        public const int RegenerationRoll = 8;

        /// <summary>As each fight begins (init_combat_party) a worn Ring of Invisibility or Regeneration vanishes on a roll of 0..15 that comes up 11.</summary>
        public static bool RingVanishes(int roll) => roll == 11;
        public const int VanishRoll = 16;

        /// <summary>The rings that may vanish: Invisibility and Regeneration.</summary>
        public static bool MayVanish(int itemId) => itemId == Invisibility || itemId == Regeneration;
        public const int Invisibility = 42, Protection = 43, Regeneration = 44, Turning = 45, SpikedCollar = 46, Ankh = 47;

        /// <summary>The bearer of the Badge's password (guard_challenge): the first four letters of the answer, any case, against "IMPE".</summary>
        public static bool PasswordMatches(DataOvl ovl, string answer)
        {
            if (answer == null) return false;
            answer = answer.Trim();
            if (answer.Length > 4) answer = answer.Substring(0, 4);
            return string.Equals(answer, ovl.CStr(Password), StringComparison.OrdinalIgnoreCase);
        }

        // ── the night sky (look_telescope) ──────────────────────────────────────────────────

        public const int SkyColumns = 22, SkyRows = 22;
        const int PlanetStart = 0x3750, PlanetRow = 0x3758, PlanetOrbit = 0x3760;

        /// <summary>One of the eight town-stars ("planets") of the night sky: its row and column in the telescope's 8-pixel grid, and whether a Shadowlord's crescent hangs beside it.</summary>
        public struct Star
        {
            public int Town, Row, Column;
            public bool Shadowlord;
            public override string ToString() => $"town {Town} at ({Row},{Column}){(Shadowlord ? " + Shadowlord" : "")}";
        }

        /// <summary>
        /// The eight town-stars on a date (year, month 1..13, day 1..28): each starts at its orbit
        /// index for 5 April 139 and steps back to its previous visible position for every day
        /// since; star p is town p+1 (Moonglow .. New Magincia), and a Shadowlord whose town it is
        /// (<paramref name="shadowlordTowns"/>, 1..8; 0 none, 0xff destroyed) shows as a crescent beside it.
        /// </summary>
        public static List<Star> NightSky(DataOvl ovl, int year, int month, int day, IList<int> shadowlordTowns)
        {
            var stars = new List<Star>();
            for (int p = 0; p < 8; p++)
            {
                int yr = year % 100, mon = month, d = day;
                int pos = ovl.U8(PlanetStart + p);
                while (yr > 39 || (yr == 39 && mon > 4) || (yr == 39 && mon == 4 && d > 5))
                {
                    if (--d == 0) { d = 28; mon--; }
                    if (mon == 0) { mon = 13; yr--; }
                    do { if (--pos < 0) pos = SkyColumns - 1; }
                    while (ovl.U8(PlanetOrbit + p * SkyColumns + pos) == 0);
                }
                bool lord = false;
                if (shadowlordTowns != null) foreach (int t in shadowlordTowns) if (t == p + 1) lord = true;
                stars.Add(new Star { Town = p + 1, Row = ovl.U8(PlanetRow + p), Column = pos + 1, Shadowlord = lord });
            }
            return stars;
        }

        // ── the looks of worn items (assets/mappings/status.json) ───────────────────────────

        /// <summary>What wearing an item shows: the Valheim status effect it looks like (its icon and effects), recoloured to the tint.</summary>
        public class Look
        {
            public int Item = -1;
            public string Name, StatusEffect, Tint, Blocks;
            public int Armour;
        }

        /// <summary>status.json: the rings' and amulets' looks ("worn", by item id) and the regalia's party effects ("party").</summary>
        public static (List<Look> worn, List<Look> party, float radiusTiles) ParseLooks(string json)
        {
            var root = Json.Obj(Json.Parse(json));
            var worn = new List<Look>();
            foreach (var e in Json.Arr(root["worn"]))
            {
                var o = Json.Obj(e);
                worn.Add(new Look { Item = (int)Json.Num(o, "item", -1), Name = Json.Str(o, "u5"), StatusEffect = Json.Str(o, "se"), Tint = Json.Str(o, "tint") });
            }
            var party = new List<Look>();
            var p = Json.Obj(root["party"]);
            foreach (var e in Json.Arr(p["effects"]))
            {
                var o = Json.Obj(e);
                party.Add(new Look { Name = Json.Str(o, "u5"), StatusEffect = Json.Str(o, "se"), Tint = Json.Str(o, "tint"), Blocks = Json.Str(o, "blocks"), Armour = (int)Json.Num(o, "armour") });
            }
            return (worn, party, (float)Json.Num(p, "radius_tiles", 1));
        }
    }
}
