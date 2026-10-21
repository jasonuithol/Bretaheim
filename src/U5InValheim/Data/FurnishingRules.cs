using System;

namespace U5InValheim.Data
{
    /// <summary>
    /// What the settlements' one-off furnishings say and do when looked at (LOOKOBJ.OVL cmd_look,
    /// look_terrain, look_well, look_fountain, look_telescope; TOWN.OVL cmd_attack_town for the
    /// mirror), in DATA.OVL's own words. The grave dig is ours (Constructing world assets.txt).
    /// </summary>
    public static class FurnishingRules
    {
        // DATA.OVL (DGROUP) strings
        public const int SWell = 0x720c, SYes = 0x7226, SThyWish = 0x722c, SNothing = 0x7238, SPoof = 0x7284, SNoEffect = 0x728c;
        /// <summary>The wishes that bring a horse (cstr table): Corvette, Ferrari, Lamborghini, Lotus, Porsche, Horse.</summary>
        public static readonly int[] HorseWords = { 0x7242, 0x724c, 0x7254, 0x7260, 0x7266, 0x726e };
        public const int SFountain = 0x729c, SRefreshing = 0x72e0;
        public const int SSun = 0x72f0, SNightSky = 0x72fa;
        public const int SPm = 0x730a, SAm = 0x7310;
        public const int SDeathVision = 0x74fa, SStrangeVision = 0x750a, SThouDostSee = 0x751c;
        public const int SBroken = 0x26f2;

        /// <summary>Tiles: the crystal sphere, the telescope, the mirror (whole, reflecting, broken), the well, the clocks.</summary>
        public const int Sphere = 0x29, Telescope = 0x59, Mirror = 0x9d, MirrorReflecting = 0x9e, MirrorBroken = 0x9f, Well = 0xa1, ClockA = 0xfa, ClockB = 0xfb;
        /// <summary>The places whose wells grant a horse: Paws and Empath Abbey.</summary>
        public const int Paws = 0x16, EmpathAbbey = 0x1f;

        public enum WishResult { Nothing, Horse, NoEffect }

        /// <summary>
        /// A wish down the well (look_well, one gold spent): an empty one is "Nothing"; a horse word
        /// anywhere in it (any case) brings a horse in Paws and Empath Abbey ("Poof!"); anything
        /// else is "No effect...". The original reads at most 12 characters.
        /// </summary>
        public static WishResult Wish(DataOvl ovl, string wish, int location)
        {
            wish = (wish ?? "").Trim();
            if (wish.Length > 12) wish = wish.Substring(0, 12);
            if (wish.Length == 0) return WishResult.Nothing;
            foreach (int w in HorseWords)
                if (wish.IndexOf(ovl.CStr(w).Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                    return location == Paws || location == EmpathAbbey ? WishResult.Horse : WishResult.NoEffect;
            return WishResult.NoEffect;
        }

        /// <summary>The words a wish gets back.</summary>
        public static string WishWords(DataOvl ovl, WishResult r)
            => ovl.CStr(r == WishResult.Nothing ? SNothing : r == WishResult.Horse ? SPoof : SNoEffect).Trim();

        /// <summary>"a grandfather clock, showing: 3:05 PM." (look_terrain: the 12-hour hour, the minutes in two digits).</summary>
        public static string ClockText(DataOvl ovl, TileNames names, int tile, int hour, int minute)
        {
            int h = hour % 12;
            if (h == 0) h = 12;
            return (names[tile] + h + ":" + minute.ToString("00") + ovl.CStr(hour < 12 ? SAm : SPm)).Trim();
        }

        /// <summary>
        /// Looking into the crystal sphere (cmd_look, tile 0x29): a roll of 1..30 at or above the
        /// looker's intelligence is a "Death vision!" (1 damage), below it a "Strange vision!"
        /// and the gem's map of where they stand.
        /// </summary>
        public static bool StrangeVision(int intelligence, int roll) => roll < intelligence;
        public const int VisionRollMin = 1, VisionRollMax = 30;

        // ── the graves (ours) ──────────────────────────────────────────────────────────────

        public enum Dig { Dirt, Body, Risen, Treasure }

        /// <summary>
        /// Digging the earth before a grave (once): a roll of 0..99: under 40 only dirt, under 75
        /// a body (bones), under 85 a body that rises (a skeleton), else treasure.
        /// </summary>
        public static Dig DigUp(int roll) => roll < 40 ? Dig.Dirt : roll < 75 ? Dig.Body : roll < 85 ? Dig.Risen : Dig.Treasure;
    }
}
