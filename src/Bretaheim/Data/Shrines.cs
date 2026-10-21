using System;
using System.IO;
using System.Text;

namespace Bretaheim.Data
{
    /// <summary>
    /// The eight shrines of virtue and the Shrine of the Codex (Rust port ovl/cast2.rs
    /// visit_shrine, shrine_quest_dialogue, codex_shrine_dialogue; ovl/cmds.rs meditate_at_shrine;
    /// ovl/mainout.rs the Codex gate): their places, virtues, mantras and words, from DATA.OVL and
    /// MISCMSG.DAT (the shrine texts, 2000 bytes from 0x3ab), and their 11×11 maps in MISCMAPS.DAT.
    /// </summary>
    public class ShrineData
    {
        public const int Count = 8, Spirituality = 6, Humility = 7;
        // DATA.OVL (DGROUP)
        const int VirtueNames = 0x1f4e, Mantras = 0x1f5e, Stems = 0x4b3e, QuestTexts = 0x4b5e, CodexTexts = 0x4b6e,
            StrFlag = 0x4b7e, DexFlag = 0x4b86, IntFlag = 0x4b8e, WordsOfPower = 0x4502;
        // MISCMSG.DAT strings, by the DGROUP address the original loads them at (0xb21e = file 0x3ab)
        const int MiscBase = 0xb21e, MiscFile = 0x3ab;
        public const int SKneel = 0xb58b, SMeditate = 0xb5b6, SUnfocused = 0xb5de, SOrdained = 0xb5ff, SQuest = 0xb62c, SReturn = 0xb669,
            SOffer = 0xb692, SNoGold = 0xb6b9, SWellDone = 0xb6d7, SBookOpen = 0xb703, SPage = 0xb733, SHowDidYou = 0xb75c,
            SWind = 0xb773, SApproach = 0xb8cc, SCodexLies = 0xb8f9;
        public static readonly int[] FinalPages = { 0xb79d, 0xb7ef, 0xb82a, 0xb89e };
        // DATA.OVL strings
        public const int SMantra = 0x958e, SAlakazam = 0x95aa, SStrength = 0x95b8, SDexterity = 0x95c6, SIntelligence = 0x95d4,
            SThouDostRead = 0x95ea, SRestored = 0x4482, SWordUttered = 0x44d7, SPassSeeker = 0x2b6e, SNotOnQuest = 0x2b7e,
            SPassageDenied = 0x2ba1, SRuins = 0x2acf;
        /// <summary>MISCMAPS.DAT: the virtue shrine's and the Codex's 11×11 maps (rows of 16 bytes).</summary>
        public const int ShrineMap = 0xb0, CodexMap = 0x160, MapSize = 11;
        /// <summary>The Codex's square on the surface, and the gate square before it (mainout: 0xe9, 0xeb).</summary>
        public const int CodexX = 233, CodexY = 233, GateX = 0xe9, GateY = 0xeb;

        readonly DataOvl ovl;
        readonly byte[] misc, maps;
        public readonly string[] Virtues = new string[Count], MantraWords = new string[Count], Stem = new string[Count], Words = new string[Count];
        public readonly int[] X = new int[Count], Y = new int[Count];
        public readonly bool[] Str = new bool[Count], Dex = new bool[Count], Int = new bool[Count];

        public ShrineData(DataOvl ovl, byte[] miscmsg, byte[] miscmaps)
        {
            this.ovl = ovl;
            misc = miscmsg;
            maps = miscmaps;
            for (int i = 0; i < Count; i++)
            {
                Virtues[i] = ovl.CStr(ovl.U16(VirtueNames + 2 * i));
                MantraWords[i] = ovl.CStr(ovl.U16(Mantras + 2 * i));
                Stem[i] = ovl.CStr(ovl.U16(Stems + 2 * i));
                Words[i] = ovl.CStr(ovl.U16(WordsOfPower + 2 * i));
                X[i] = ovl.U8(Dg.ShrineX + i);
                Y[i] = ovl.U8(Dg.ShrineY + i);
                Str[i] = ovl.U8(StrFlag + i) != 0;
                Dex[i] = ovl.U8(DexFlag + i) != 0;
                Int[i] = ovl.U8(IntFlag + i) != 0;
            }
        }

        public static ShrineData Load(string dir, DataOvl ovl)
            => new ShrineData(ovl, File.ReadAllBytes(U5Data.FindFile(dir, "MISCMSG.DAT")), File.ReadAllBytes(U5Data.FindFile(dir, "MISCMAPS.DAT")));

        public static bool Available(string dir) => U5Data.FindFile(dir, "MISCMSG.DAT") != null && U5Data.FindFile(dir, "MISCMAPS.DAT") != null;

        /// <summary>The shrine on a surface square, or −1.</summary>
        public int At(int x, int y)
        {
            for (int i = 0; i < Count; i++) if (X[i] == x && Y[i] == y && !(x == 0 && y == 0)) return i;
            return -1;
        }

        /// <summary>A MISCMSG.DAT string by its DGROUP address.</summary>
        public string Msg(int dg) => CStrAt(misc, MiscFile + dg - MiscBase);
        /// <summary>A DATA.OVL string.</summary>
        public string Ovl(int dg) => ovl.CStr(dg);

        /// <summary>What the Codex's quest is "to learn" (MISCMSG via 0x4b5e: "the failing of Dishonesty!").</summary>
        public string Quest(int i) => CStrAt(misc, MiscFile + ovl.U16(QuestTexts + 2 * i));
        /// <summary>The Codex's words on a virtue (MISCMSG via 0x4b6e).</summary>
        public string CodexWords(int i) => CStrAt(misc, MiscFile + ovl.U16(CodexTexts + 2 * i));

        /// <summary>A tile of the shrine's (or the Codex's) map.</summary>
        public byte MapTile(bool codex, int x, int y) => maps[(codex ? CodexMap : ShrineMap) + y * 16 + x];

        static string CStrAt(byte[] d, int o)
        {
            var sb = new StringBuilder();
            for (; o >= 0 && o < d.Length && d[o] != 0; o++) sb.Append((char)(d[o] & 0x7f));
            return sb.ToString();
        }

        /// <summary>
        /// The Codex's last pages are in runes (font 1): '@' a space, '[' TH, '^' EA, '_' ST,
        /// ']' NG, 'o' the end of a sentence. Read in letters.
        /// </summary>
        public static string Runes(string s)
            => s.Replace("@", " ").Replace("[", "TH").Replace("^", "EA").Replace("_", "ST").Replace("]", "NG").Replace("o\n", ".\n").TrimEnd('o');
    }

    /// <summary>What meditating at a shrine comes to (shrine_quest_dialogue).</summary>
    public enum ShrineOutcome { Unfocused, Ordained, Offering, WellDone }

    /// <summary>
    /// The rules of the shrines, as the original has them (Rust ovl/cast2.rs): the answer must
    /// hold the virtue's stem (its first four letters) and each of the three mantras the mantra
    /// (case-insensitive, anywhere in the line). Then, by the quest bits (ordained, done):
    /// a quest not yet done is ordained; a quest done and its reward taken asks an offering of
    /// gold (karma + hundreds); a quest done and ordained is rewarded ("WELL DONE!": karma + 3,
    /// the virtue's +1 strength/dexterity/intelligence up to 30, Humility another karma + 3) and
    /// its ordination cleared. The Codex completes the lowest ordained quest; with all eight
    /// done it shows its last pages.
    /// </summary>
    public static class ShrineRules
    {
        public const string QuestsKey = "u5_shrine_quests", DoneKey = "u5_shrine_done";
        public const int WellDoneKarma = 3, HumilityKarma = 3, MaxStat = 30;

        static bool Holds(string line, string word) => !string.IsNullOrEmpty(line) && line.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>The virtue named and the three mantras chanted rightly.</summary>
        public static bool Focused(ShrineData d, int shrine, string virtue, string[] mantras)
        {
            bool ok = Holds(virtue, d.Stem[shrine]);
            if (mantras == null || mantras.Length < 3) return false;
            for (int k = 0; k < 3; k++) if (!Holds(mantras[k], d.MantraWords[shrine])) ok = false;
            return ok;
        }

        /// <summary>Restoring a ruined shrine (meditate_at_shrine): the virtue's full name and the mantra three times.</summary>
        public static bool Restores(ShrineData d, int shrine, string virtue, string[] mantras)
        {
            bool ok = Holds(virtue, d.Virtues[shrine]);
            if (mantras == null || mantras.Length < 3) return false;
            for (int k = 0; k < 3; k++) if (!Holds(mantras[k], d.MantraWords[shrine])) ok = false;
            return ok;
        }

        /// <summary>The outcome of a focused meditation, updating the ordained quests.</summary>
        public static ShrineOutcome Meditate(int shrine, ref int quests, int done)
        {
            int bit = 1 << shrine;
            if ((done & bit) == 0) { quests |= bit; return ShrineOutcome.Ordained; }
            if ((quests & bit) == 0) return ShrineOutcome.Offering;
            quests &= ~bit;
            return ShrineOutcome.WellDone;
        }

        /// <summary>"WELL DONE!": the karma and the stats (returns which rose: s, d, i).</summary>
        public static (bool str, bool dex, bool intel) Reward(ShrineData d, int shrine, ref int karma, Avatar a)
        {
            karma = Math.Min(99, karma + WellDoneKarma);
            bool s = d.Str[shrine], x = d.Dex[shrine], i = d.Int[shrine];
            if (s) a.Strength = Math.Min(MaxStat, a.Strength + 1);
            if (x) a.Dexterity = Math.Min(MaxStat, a.Dexterity + 1);
            if (i) a.Intelligence = Math.Min(MaxStat, a.Intelligence + 1);
            if (shrine == ShrineData.Humility) karma = Math.Min(99, karma + HumilityKarma);
            return (s, x, i);
        }

        /// <summary>An offering of <paramref name="hundreds"/> hundred gold (1..9): false when not that much is carried; else karma + hundreds.</summary>
        public static bool Offer(int hundreds, int gold, ref int karma)
        {
            if (hundreds * 100 > gold) return false;
            karma = Math.Min(99, karma + hundreds);
            return true;
        }

        /// <summary>The Codex gate: passage only upon a sacred quest.</summary>
        public static bool MayPass(int quests) => quests != 0;

        /// <summary>The Codex reads the lowest ordained quest (marking it done) or −1 ("HOW DID YOU GET HERE?"); all eight done: the last pages.</summary>
        public static int ReadCodex(int quests, ref int done, out bool final)
        {
            final = false;
            for (int i = 0; i < ShrineData.Count; i++)
            {
                if ((quests & (1 << i)) == 0) continue;
                done |= 1 << i;
                final = (done & 0xff) == 0xff;
                return i;
            }
            return -1;
        }
    }
}
