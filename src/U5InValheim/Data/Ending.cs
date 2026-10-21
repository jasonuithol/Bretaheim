using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace U5InValheim.Data
{
    /// <summary>
    /// ENDGAME.OVL endgame_sequence (Rust port ovl/endgame.rs), as a cut-scene: through the mirror
    /// of Doom's last room the party is in Lord British's prison (MISCMAPS.DAT offset 0x210);
    /// Lord British walks up, the party comes in, "Didst thou bring my box?" (ENDMSG.DAT). With the
    /// Sandalwood Box he opens it, speaks of the Orb of the Moons, casts it down, a moongate rises
    /// and everyone follows him through it; the six end-story screens (<see cref="EndStory"/>) and
    /// the certificate (<see cref="Certificate"/>) follow. Without it: "I see..." and the figures
    /// wander the cell (for ever, in the original).
    /// </summary>
    public class EndgameScene
    {
        /// <summary>Doom's last room (DUNGEON.CBT room 15 of Doom, file index 111) and its mirror (tile 0x9d at x 5, y 1): stepping into it, a member "is absorbed!" (SJOG combat_check_absorbed).</summary>
        public const int Dungeon = 7, Room = 15, MirrorX = 5, MirrorY = 1, MirrorTile = 0x9d;
        public const int SceneMap = 0x210;
        const int Classes = 0x701a, ClassTiles = 0x1ade, MemberX = 0x3e5a, MemberY = 0x3e60;
        // DATA.OVL strings
        public const int SAbsorbed = 0x8f02;
        const int SLives = 0x849a, SClose = 0x84ae, SYes = 0x84b4, SNo = 0x84ba, SHeSays = 0x84cc, SISee = 0x84da;
        // ENDMSG.DAT (loaded at 0xb21e)
        public const int MWellMet = 0xb21e, MBox = 0xb23f, MBoxAgain = 0xb267, MOpens = 0xb2c9, MFollow = 0xb469, MChair = 0xb4f3;
        public static readonly int[] MPages = { 0xb2f1, 0xb346, 0xb385, 0xb3e7, 0xb42f };
        public const byte LordBritishTile = 0x7c, BoxTile = 0x0e, OpenBoxTile = 0x08, Slot = 0x1f, BoxSlot = 6;

        readonly DataOvl ovl;
        readonly byte[] maps, msg;
        readonly IList<string> members;
        readonly bool box;
        readonly Random random;
        public readonly CutScene View = new CutScene();
        /// <summary>The answer after a <see cref="BeatKind.YesNo"/> beat.</summary>
        public bool Yes;
        /// <summary>Everyone has gone through the gate with Lord British: the game is won.</summary>
        public bool Won;
        public Action OnWon;
        /// <summary>The end-story screens and the certificate, made when they are reached.</summary>
        public Func<IEnumerable<Beat>> Finale;

        /// <param name="members">The party's names, the Avatar first (up to six).</param>
        /// <param name="box">The Sandalwood Box is carried (g_sandalwood_box).</param>
        public EndgameScene(DataOvl ovl, byte[] miscmaps, byte[] endmsg, IList<string> members, bool box, Random random)
        {
            this.ovl = ovl;
            maps = miscmaps;
            msg = endmsg;
            this.members = members.Count > 6 ? new List<string>(members).GetRange(0, 6) : members;
            this.box = box;
            this.random = random ?? new Random();
        }

        public string Msg(int dg) => CutScene.StrAt(msg, dg);
        string S(int dg) => ovl.CStr(dg);

        byte ClassTile(char c) => ovl.U8(ClassTiles + Math.Max(0, ovl.CStr(Classes).IndexOf(c)));

        public IEnumerable<Beat> Run()
        {
            var v = View;
            v.LoadMap(maps, SceneMap);
            v.ClearAll();
            v.Set(Slot, LordBritishTile, 5, 8);
            yield return Beat.Wait(0x28);
            foreach (var b in v.Walk(Slot, 5, 3)) yield return b;
            for (int m = 0; m < members.Count; m++)
            {
                v.Set(m, ClassTile('A'), 5, 9);
                yield return Beat.Wait(1);
                foreach (var b in v.Walk(m, ovl.U8(MemberX + m), ovl.U8(MemberY + m))) yield return b;
            }
            yield return Beat.Wait(0x28);
            v.Print(Msg(MWellMet) + members[0] + S(SClose));
            yield return Beat.Key;
            v.Print(Msg(MBox));
            yield return new Beat { Kind = BeatKind.YesNo };
            v.Print(Yes ? S(SYes) : S(SNo));
            if (!Yes)
            {
                v.Print(Msg(MBoxAgain));
                yield return new Beat { Kind = BeatKind.YesNo };
                v.Print(Yes ? S(SYes) : S(SNo));
            }
            if (Yes && box)
            {
                yield return Beat.Wait(8);
                foreach (var b in v.Walk(0, 5, 4)) yield return b;
                foreach (var b in v.Walk(0, 5, 5)) yield return b;
                yield return Beat.Wait(4);
                v.Set(BoxSlot, BoxTile, 5, 4);
                v.Print(Msg(MOpens));
                yield return Beat.Wait(0x28);
                v.Print(S(SHeSays));
                yield return Beat.Key;
                foreach (var page in MPages)
                {
                    v.Print(Msg(page));
                    yield return Beat.Key;
                }
                v.Print(Msg(MFollow));
                v.Tile[BoxSlot] = OpenBoxTile;
                yield return Beat.Key;
                v.Clear(BoxSlot);
                // the Orb of the Moons: a moongate rises where the box lay
                v.GateX = 5; v.GateY = 4;
                for (int h = 1; h <= 16; h++)
                {
                    v.GateHeight = h;
                    yield return Beat.Wait(1);
                }
                yield return Beat.Wait(4);
                foreach (var b in v.Walk(Slot, 5, 4)) yield return b;
                v.Clear(Slot);
                yield return Beat.Wait(1);
                for (int m = 0; m < members.Count; m++)
                {
                    foreach (var b in v.Walk(m, 5, 4)) yield return b;
                    v.Clear(m);
                    yield return Beat.Wait(1);
                }
                Won = true;
                OnWon?.Invoke();
                for (int h = 15; h >= 0; h--)
                {
                    v.GateHeight = h;
                    yield return Beat.Wait(1);
                }
                v.Poke(5, 4, 0x44);
                v.Dark = true;
                yield return Beat.Wait(8);
                if (Finale != null) foreach (var b in Finale()) yield return b;
                yield break;
            }
            v.Print(S(SISee));
            yield return Beat.Wait(0x28);
            v.Print(Msg(MChair));
            v.Y[0]--;
            yield return Beat.Step;
            while (true)
            {
                bool a = v.StepToward(2, 8, 6), b = v.StepToward(Slot, 4, 1), c = v.StepToward(0, 8, 4);
                if (!a && !b && !c) break;
                yield return Beat.Step;
            }
            // they wander the cell for ever
            while (true)
            {
                foreach (int s in new[] { 1, 3, 4, 5 }) Wander(s);
                yield return new Beat { Kind = BeatKind.Wait, Seconds = Beat.Tick * 3, Endless = true };
            }
        }

        /// <summary>wander_slot: a figure steps at random onto the floor (tile 0x44), now and then.</summary>
        void Wander(int slot)
        {
            var v = View;
            if (v.Tile[slot] == 0 || random.Next(2) == 0) return;
            for (int t = 0; t < 8; t++)
            {
                int x = v.X[slot], y = v.Y[slot];
                switch (random.Next(4))
                {
                    case 0: x++; break;
                    case 1: x--; break;
                    case 2: y++; break;
                    default: y--; break;
                }
                if ((uint)x < CutScene.Size && (uint)y < CutScene.Size && v.Map[y, x] == 0x44)
                {
                    v.X[slot] = x;
                    v.Y[slot] = y;
                    return;
                }
            }
        }
    }

    /// <summary>
    /// ENDGAME end_story_screens: the six screens of the ending, drawn as the original draws them
    /// (DATA.OVL g_end_* tables): a picture of END1.16/END2.16, the TEXT.16 headings on screens 0
    /// and 3, a paragraph of END.DAT in the proportional font; then ENDSC.16.
    /// </summary>
    public static class EndStory
    {
        public const int Screens = 6;

        public static bool Available(string dir)
        {
            foreach (var f in new[] { "PROPORT.PCS", "TEXT.16", "END.DAT", "END1.16", "END2.16", "ENDSC.16", "ENDMSG.DAT", "MISCMAPS.DAT", "MISCMSG.DAT", "IBM.CH", "RUNES.CH" })
                if (U5Data.FindFile(dir, f) == null) return false;
            return true;
        }

        public static List<StoryScreen> Load(string dir, DataOvl ovl)
        {
            byte[] Read(string name) => File.ReadAllBytes(U5Data.FindFile(dir, name));
            var font = new ProportionalFont(Read("PROPORT.PCS"), ovl);
            var text = new PictureFile(Read("TEXT.16"));
            var endDat = Read("END.DAT");
            var files = new Dictionary<int, PictureFile>();
            var r = new List<StoryScreen>();
            for (int s = 0; s < Screens; s++)
            {
                int number = ovl.U8(Dg.EndStoryNumber + s);
                if (!files.TryGetValue(number, out var pics)) files[number] = pics = new PictureFile(Read($"END{number + 1}.16"));
                var screen = new Screen();
                if (ovl.U8(Dg.EndHook + s) == 1)
                {
                    if (s == 0) { screen.Draw(text[0], 0xd8, 0); screen.Draw(text[4], 0x98, 0x1c); }
                    else if (s == 3) { screen.Draw(text[5], 0xe0, 0); screen.Draw(text[0], 0xb0, 0); }
                }
                screen.Draw(pics[ovl.U8(Dg.EndImageIndex + s)], ovl.U8(Dg.EndPicX + s), ovl.U8(Dg.EndPicY + s));
                var layout = new TextLayout
                {
                    Left0 = ovl.U8(Dg.EndLeftX + s * 2), Left1 = ovl.U8(Dg.EndLeftX + s * 2 + 1),
                    Right0 = ovl.U16(Dg.EndRightX + s * 4), Right1 = ovl.U16(Dg.EndRightX + s * 4 + 2),
                    Band1From = ovl.U8(Dg.EndPara1Y + s), Band1To = ovl.U8(Dg.EndPara2Y + s),
                    X = ovl.U8(Dg.EndFirstLineX + s), Y = ovl.U8(Dg.EndTextY + s), Space = font.DefaultSpace,
                };
                var para = Screen.TextAt(endDat, ovl.U16(Dg.EndTextOffsets + s * 2));
                screen.Text(para, font, layout);
                r.Add(new StoryScreen { Picture = screen, Text = Screen.Plain(para).Trim() });
            }
            var last = new Screen();
            last.Draw(new PictureFile(Read("ENDSC.16"))[0], 0x28, 0);
            r.Add(new StoryScreen { Picture = last, Text = "" });
            return r;
        }
        /// <summary>The ending after the gate: the six screens, then the certificate over the scroll (the original then stops for ever).</summary>
        public static IEnumerable<Beat> Finale(string dir, DataOvl ovl, string avatar, int year, int month, int day)
        {
            var screens = Load(dir, ovl);
            for (int i = 0; i < Screens; i++) yield return new Beat { Kind = BeatKind.Picture, Picture = screens[i].Picture, Words = screens[i].Text };
            byte[] Read(string name) => File.ReadAllBytes(U5Data.FindFile(dir, name));
            var lines = Certificate.Lines(ovl, avatar, year, month, day);
            var page = Certificate.Render(lines, new FixedFont(Read("IBM.CH")), new FixedFont(Read("RUNES.CH")), screens[Screens].Picture);
            yield return new Beat { Kind = BeatKind.Picture, Picture = page, Words = Certificate.Plain(lines), Endless = true };
        }
    }

    /// <summary>
    /// ENDGAME print_quest_report: "Be it known that on the … Day of the … Month of the Year …
    /// Hundred … <name> the Avatar saved the life of our sovereign Lord British …", "THE QUEST OF
    /// THE AVATAR IS FOREVER" in runes, and the time the quest took since the first day (4-5-139).
    /// </summary>
    public static class Certificate
    {
        const int Numbers = 0x3e0a, Tens = 0x3e2e, Ordinals = 0x3e40, Ordinals20 = 0x3e18;
        const int SBeIt = 0x8332, SThe = 0x8348, SDayOf = 0x834e, SThe2 = 0x8358, SMonth = 0x835e, SOfTheYear = 0x8366, SHundred = 0x8374,
            SBlank = 0x837e, STheAvatar = 0x8382, SReport = 0x8414, SYear = 0x8438, SS = 0x843e, SComma = 0x8440, SMonthW = 0x8444,
            SS2 = 0x844c, SComma2 = 0x844e, SDay = 0x8452, SS3 = 0x8458, SNl = 0x845a, SOrigin = 0x845c, SHyphen = 0x82c6,
            STh = 0x831e, STwent = 0x8322, SIeth = 0x8328, SYHyphen = 0x832e;
        static readonly int[] Saved = { 0x8390, 0x83a0, 0x83b2, 0x83ca, 0x83de };
        /// <summary>"THE QUEST OF THE AVATAR" / "IS FOREVER", printed in the runic font.</summary>
        public static readonly int[] Runic = { 0x83ee, 0x8404 };
        /// <summary>The quest's first day (print_quest_report counts from it).</summary>
        public const int StartYear = 139, StartMonth = 4, StartDay = 5;

        static string P(DataOvl ovl, int table, int i)
        {
            int p = ovl.U16(table + 2 * i);
            return p == 0 ? "" : ovl.CStr(p);
        }

        /// <summary>append_number_word: 0..99 in words ("Thirty-Nine").</summary>
        public static string Number(DataOvl ovl, int n)
        {
            if (n > 20)
            {
                var tens = P(ovl, Tens, n / 10);
                return n % 10 == 0 ? tens : tens + ovl.CStr(SHyphen) + P(ovl, Numbers, n % 10);
            }
            return P(ovl, Numbers, n);
        }

        /// <summary>append_ordinal_word: 1..28 ("Fifth", "Thirteenth", "Twentieth", "Twenty-Third").</summary>
        public static string Ordinal(DataOvl ovl, int n)
        {
            if (n < 13) return P(ovl, Ordinals, n);
            if (n < 20) return Number(ovl, n) + ovl.CStr(STh);
            if (n == 20) return ovl.CStr(STwent) + ovl.CStr(SIeth);
            return ovl.CStr(STwent) + ovl.CStr(SYHyphen) + P(ovl, Ordinals20, n);
        }

        /// <summary>How long the quest took: (years, months, days) since 4-5-139, as the original reckons it (28-day months, 13 months).</summary>
        public static (int years, int months, int days) Elapsed(int year, int month, int day)
        {
            int years = year - StartYear, months = month - StartMonth, days = day - StartDay;
            if (days < 0) { days = day + 23; months = month - 5; }
            if (months < 0) { months += 13; years = year - (StartYear + 1); }
            return (years, months, days);
        }

        /// <summary>The certificate's lines (runic: in the runic font).</summary>
        public static List<(string text, bool runic)> Lines(DataOvl ovl, string avatar, int year, int month, int day)
        {
            var r = new List<(string, bool)>();
            void Add(string s)
            {
                foreach (var line in s.Split('\n')) r.Add((line, false));
            }
            string S(int dg) => ovl.CStr(dg);
            var b = new StringBuilder();
            b.Append(S(SBeIt));
            b.Append(S(SThe)).Append(Ordinal(ovl, day)).Append(S(SDayOf));
            b.Append(S(SThe2)).Append(Ordinal(ovl, month)).Append(S(SMonth));
            b.Append(S(SOfTheYear));
            b.Append(Number(ovl, year / 100)).Append(S(SHundred));
            b.Append(Number(ovl, year % 100)).Append(S(SBlank));
            b.Append(avatar).Append(S(STheAvatar));
            foreach (int s in Saved) b.Append(S(s));
            var text = b.ToString();
            if (text.EndsWith("\n")) text = text.Substring(0, text.Length - 1);
            Add(text);
            var runes = (S(Runic[0]) + S(Runic[1])).Split('\n');
            for (int i = 0; i < runes.Length - 1; i++) r.Add((runes[i], runes[i].Length > 0));
            var (years, months, days) = Elapsed(year, month, day);
            var t = new StringBuilder(S(SReport));
            if (years != 0)
            {
                t.Append(years).Append(S(SYear));
                if (years > 1) t.Append(S(SS));
                if (months != 0 || days != 0) t.Append(S(SComma));
            }
            if (months != 0)
            {
                t.Append(months).Append(S(SMonthW));
                if (months > 1) t.Append(S(SS2));
                if (days != 0) t.Append(S(SComma2));
            }
            if (days != 0)
            {
                t.Append(days).Append(S(SDay));
                if (days > 1) t.Append(S(SS3));
            }
            t.Append(S(SNl)).Append(S(SOrigin));
            Add(t.ToString());
            return r;
        }

        /// <summary>The certificate in plain words (the runes read as letters).</summary>
        public static string Plain(List<(string text, bool runic)> lines)
        {
            var b = new StringBuilder();
            foreach (var (text, runic) in lines) b.Append(runic ? ShrineData.Runes(text) : text).Append('\n');
            return b.ToString();
        }

        /// <summary>The certificate on the screen: printed over the scroll of ENDSC.16 (<paramref name="scroll"/>) from the second row, each line centred in 40 columns of IBM.CH, the runes in RUNES.CH.</summary>
        public static Screen Render(List<(string text, bool runic)> lines, FixedFont ibm, FixedFont runes, Screen scroll = null)
        {
            var s = new Screen();
            if (scroll != null) Array.Copy(scroll.Pixels, s.Pixels, s.Pixels.Length);
            int y = FixedFont.Size;
            foreach (var (text, runic) in lines)
            {
                int x = (Screen.W - text.Length * FixedFont.Size) / 2;
                (runic ? runes : ibm).Draw(s, text, x, y, runic ? 4 : 6);
                y += FixedFont.Size;
            }
            return s;
        }
    }
}
