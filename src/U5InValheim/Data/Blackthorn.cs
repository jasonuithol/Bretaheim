using System;
using System.Collections.Generic;

namespace U5InValheim.Data
{
    /// <summary>
    /// BLCKTHRN.OVL blackthorn_dungeon_scene (Rust port ovl/blckthrn.rs), played as a cut-scene:
    /// arrested in Blackthorn's palace, the party is subdued, blindfolded and dragged to the cell of
    /// MISCMAPS.DAT (offset 0), chained at its seats; guards come, Blackthorn appears and asks the
    /// mantra of the first shrine not yet betrayed (g_shrine_destroyed), up to four times
    /// (MISCMSG.DAT texts). The mantra anywhere in the (14-character) answer betrays the shrine
    /// (5 karma), and with a companion he still grants the companion "a merciful death"; refusing
    /// alone: "To the dungeon with thee!"; refusing with a companion: the companion is threatened,
    /// laid under the pendulum, cut and at the fourth refusal sliced in half. Either way the party
    /// is left in the palace's dungeon at (10, 7). With every shrine already betrayed nothing is asked.
    /// </summary>
    public class BlackthornScene
    {
        public const int Location = 0x12, ExitX = 10, ExitY = 7, ExitZ = -1, AnswerLength = 14;
        // DATA.OVL (DGROUP) strings
        const int SSubdued = 0x6fbc, SDragged = 0x6fe0, SChained = 0x7024, SFootsteps = 0x704c, SSays = 0x705a, SHonour = 0x7074,
            SGuard = 0x70a4, SLady = 0x70c0, SMan = 0x70c8, SAtOnce = 0x70ce, SQuote = 0x6f6e, SResponse = 0x6f7a,
            SSliced = 0x6f96, SDie = 0x6fac;
        const int Classes = 0x701a, ClassTiles = 0x1ade, Seats = 0x1f0a, SeatX = 0x1f42, SeatY = 0x1f48;
        const int VirtueNames = 0x1f4e, Mantras = 0x1f5e;
        /// <summary>The move scripts: release, threaten a companion, guards enter, Blackthorn speaks, Blackthorn leaves.</summary>
        public const int ScriptRelease = 0x369e, ScriptThreaten = 0x36da, ScriptGuards = 0x3702, ScriptSpeaks = 0x370e, ScriptLeaves = 0x3716;
        // MISCMSG.DAT (loaded at 0xb21e)
        public const int MAsk0 = 0xb21e, MAsk1 = 0xb24b, MAsk2 = 0xb270, MAsk3 = 0xb2ca, MPendulum = 0xb30f, MMerciful = 0xb34b,
            MUnfair = 0xb3be, MLaughing = 0xb447, MSand = 0xb47d, MTruth = 0xb4ba, MChild = 0xb4fa, MWait = 0xb54a;
        /// <summary>Monster-table tiles: a guard, Blackthorn, the sparkle he appears in.</summary>
        public const byte GuardTile = 0x70, BlackthornTile = 0x78, SparkleTile = 0x16;

        readonly DataOvl ovl;
        readonly byte[] msg, maps;
        readonly string avatar, companion;
        readonly bool female;
        readonly int living;
        public readonly CutScene View = new CutScene();
        /// <summary>The virtue asked about (−1: every shrine is betrayed already).</summary>
        public readonly int Virtue;
        /// <summary>The answer typed after an <see cref="BeatKind.Ask"/> beat.</summary>
        public string Answer;
        /// <summary>What came of it.</summary>
        public bool Betrayed, CompanionKilled;
        /// <summary>The shrine betrayed (Shrines.Desecrate and 5 karma); the companion executed (sliced: by the pendulum).</summary>
        public Action<int> OnBetray;
        public Action<bool> OnExecute;

        /// <param name="living">The Avatar and their living companions (the party).</param>
        /// <param name="betrayed">g_shrine_destroyed: the shrines already ruined.</param>
        public BlackthornScene(DataOvl ovl, byte[] miscmsg, byte[] miscmaps, string avatar, bool female, int living, string companion, bool[] betrayed)
        {
            this.ovl = ovl;
            msg = miscmsg;
            maps = miscmaps;
            this.avatar = avatar;
            this.female = female;
            this.living = Math.Max(1, Math.Min(6, living));
            this.companion = companion ?? "";
            Virtue = FirstUnbetrayed(betrayed);
        }

        public string Msg(int dg) => CutScene.StrAt(msg, dg);
        string S(int dg) => ovl.CStr(dg);
        public string VirtueName(int v) => ovl.CStr(ovl.U16(VirtueNames + 2 * v));
        public string Mantra(int v) => ovl.CStr(ovl.U16(Mantras + 2 * v));

        /// <summary>The first shrine not yet betrayed, −1 when all eight are.</summary>
        public static int FirstUnbetrayed(bool[] betrayed)
        {
            for (int v = 0; v < 8; v++) if (betrayed == null || v >= betrayed.Length || !betrayed[v]) return v;
            return -1;
        }

        /// <summary>response_contains_mantra: the mantra anywhere in the first 14 characters typed, any case.</summary>
        public static bool ContainsMantra(string answer, string mantra)
        {
            if (string.IsNullOrEmpty(mantra)) return false;
            answer = answer ?? "";
            if (answer.Length > AnswerLength) answer = answer.Substring(0, AnswerLength);
            return answer.IndexOf(mantra, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>The figure tile of a class letter ("AMBFDTPRS", the players are Avatars).</summary>
        byte ClassTile(char c)
        {
            var letters = ovl.CStr(Classes);
            int i = Math.Max(0, letters.IndexOf(c));
            return ovl.U8(ClassTiles + i);
        }

        /// <summary>The seat of party member <paramref name="i"/> of <paramref name="count"/>.</summary>
        public (int x, int y) Seat(int count, int i)
        {
            int seat = ovl.U8(Seats + count * 8 + i);
            return (ovl.U8(SeatX + seat), ovl.U8(SeatY + seat));
        }

        public IEnumerable<Beat> Run()
        {
            var v = View;
            v.Dark = true;
            v.Print(S(SSubdued));
            if (Virtue >= 0)
            {
                yield return Beat.Wait(2);
                for (int i = 0; i < 5; i++) yield return Beat.Wait(5, true);
                v.Print(S(SDragged));
                for (int i = 0; i < 18; i++) yield return Beat.Wait(5, true);
                v.ClearAll();
                v.LoadMap(maps, 0);
                for (int i = 0; i < living; i++)
                {
                    var (x, y) = Seat(living, i);
                    v.Set(i, ClassTile('A'), x, y);
                }
                yield return Beat.Wait(0x10);
                v.Print(S(SChained));
                yield return Beat.Wait(0x32);
                v.Print(S(SFootsteps));
                for (int i = 0; i < 8; i++) yield return Beat.Step;
                v.Set(6, GuardTile, 4, 10);
                v.Set(7, GuardTile, 6, 10);
                foreach (var b in v.RunScript(ovl, ScriptGuards)) yield return b;
                v.Set(8, SparkleTile, 5, 5);
                yield return Beat.Wait(8);
                v.Set(8, BlackthornTile, 5, 5);
                yield return Beat.Wait(8);
                v.Print(S(SSays) + avatar + S(SHonour));
                yield return Beat.Key;
                v.Print(S(SGuard) + (female ? S(SLady) : S(SMan)) + S(SAtOnce));
                foreach (var b in v.RunScript(ovl, ScriptSpeaks)) yield return b;
                v.Print(Msg(MWait));
                yield return Beat.Key;
                foreach (var b in Interrogation()) yield return b;
                if (v.Tile[8] != 0)
                    foreach (var b in v.RunScript(ovl, ScriptLeaves)) yield return b;
            }
            yield return Beat.Key;
        }

        IEnumerable<Beat> Interrogation()
        {
            var v = View;
            bool threatened = false;
            v.Print("\n\n");
            for (int round = 0; round < 4; round++)
            {
                v.Print(Question(round));
                v.Print(S(SResponse));
                Answer = "";
                yield return new Beat { Kind = BeatKind.Ask, Prompt = "Your response?" };
                var said = (Answer ?? "").Trim();
                if (said.Length > AnswerLength) said = said.Substring(0, AnswerLength);
                v.Print(said + "\n\n");
                if (ContainsMantra(said, Mantra(Virtue)))
                {
                    Betrayed = true;
                    OnBetray?.Invoke(Virtue);
                    if (living > 1) foreach (var b in Execute(false)) yield return b;
                    else v.Print(Msg(MTruth));
                    foreach (var b in Release()) yield return b;
                    yield break;
                }
                if (living < 2)
                {
                    v.Print(Msg(MChild));
                    foreach (var b in Release()) yield return b;
                    yield break;
                }
                if (threatened)
                {
                    // two minutes pass while the pendulum swings lower
                    if (round == 1) v.Poke(5, 9, 0xeb);
                    else if (round == 2) v.Poke(5, 9, 0xe8);
                    else if (round == 3) foreach (var b in Execute(true)) yield return b;
                    yield return Beat.Wait(8);
                }
                else
                {
                    threatened = true;
                    v.Print(Msg(MLaughing));
                    foreach (var b in v.RunScript(ovl, ScriptThreaten)) yield return b;
                    v.Print(Msg(MSand) + companion + S(SDie));
                    yield return Beat.Key;
                    v.Print("\n\n");
                }
            }
        }

        /// <summary>print_interrogation_question.</summary>
        public string Question(int round)
        {
            switch (round)
            {
                case 0: return Msg(MAsk0) + VirtueName(Virtue) + S(SQuote);
                case 1: return Msg(MAsk1) + VirtueName(Virtue) + S(SQuote);
                case 2: return Msg(MAsk2) + VirtueName(Virtue) + S(SQuote);
                default: return Msg(MAsk3);
            }
        }

        /// <summary>execute_prisoner: the companion dies (party member two; in the cell the figure on the pendulum's table).</summary>
        IEnumerable<Beat> Execute(bool sliced)
        {
            var v = View;
            v.Print(sliced ? Msg(MPendulum) : Msg(MMerciful));
            yield return Beat.Wait(10);
            yield return Beat.Wait(20);
            v.Clear(1);
            v.Poke(5, 7, 0x80);
            CompanionKilled = true;
            OnExecute?.Invoke(sliced);
            if (sliced)
            {
                v.Print("\n\n" + companion + S(SSliced));
                yield return Beat.Key;
                v.Print(Msg(MUnfair));
            }
            v.Print("\n");
        }

        /// <summary>script_release: a key, and the guards lead the party out.</summary>
        IEnumerable<Beat> Release()
        {
            yield return Beat.Key;
            foreach (var b in View.RunScript(ovl, ScriptRelease)) yield return b;
        }
    }
}
