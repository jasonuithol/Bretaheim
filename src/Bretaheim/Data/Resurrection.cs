using System;
using System.Collections.Generic;
using System.Text;

namespace Bretaheim.Data
{
    /// <summary>
    /// BLCKTHRN resurrection_scene (Rust port ovl/blckthrn.rs), when the whole party has fallen:
    /// Lord British raises them in his castle. Nothing carried is lost. Each member is raised as
    /// In Mani Corp raises (CAST2 in_mani_corp_resurrect, mode 0xff): experience × karma / 100
    /// unless karma is 98 or more, the level that experience earns (it may fall; strength,
    /// dexterity and intelligence stay), full hit points, mana = intelligence (the Avatar's
    /// class). Then karma is at least 75, the party stands in the throne room (castle z 1, 10,
    /// 10), time passes to 6 o'clock, light and protection end, and an empty larder holds 63
    /// rations.
    /// </summary>
    public static class Resurrection
    {
        public const int KarmaFloor = 75, WakeHour = 6, Food = 63;

        /// <summary>The Avatar raised (in_mani_corp_resurrect), by the karma before the scene raises it.</summary>
        public static void Raise(Avatar a, int karma)
        {
            a.Experience = Magic.ExperienceAfterResurrection(a.Experience, karma);
            a.Level = Avatar.LevelFor(a.Experience);
            a.Mana = a.Intelligence;
        }

        /// <summary>The karma afterwards: at least 75.</summary>
        public static int KarmaAfter(int karma) => Math.Max(karma, KarmaFloor);
    }

    /// <summary>
    /// The scene of <see cref="Resurrection"/> in the original's map window: darkness ("An
    /// unending darkness engulfs thee..."), the sleeper in the middle, two sparkles that leave
    /// lights at either side, a peal of thunder and Lord British above, who speaks the KARMA.DAT
    /// paragraph for the karma (one of five, by karma / 20); "Strange words are intoned.",
    /// "Vertigo..." and only the sleeper is left.
    /// </summary>
    public class ResurrectionScene
    {
        // DATA.OVL strings
        public const int SDarkness = 0x70e2, SRefuge = 0x7108, SNoEvil = 0x7122, SDisturbed = 0x7152, SShouts = 0x7172,
                         SThunder = 0x719e, SQuote = 0x71be, SIntoned = 0x71cc, SVertigo = 0x71ea;
        /// <summary>g_karma_text_offsets (u16[5]): KARMA.DAT paragraph offsets by karma / 20.</summary>
        const int KarmaTexts = 0x1a74;
        /// <summary>The map window's tiles (g_light_map) and figures (slot tile bytes, drawn + 0x100).</summary>
        public const byte Darkness = 0xff, Sleeper = 0x1c, Sparkle = 0x16, LordBritish = 0x74, LeftLight = 0x5e, RightLight = 0x5f;

        readonly DataOvl ovl;
        readonly byte[] karmaDat;
        readonly int karma;
        public readonly CutScene View = new CutScene();

        /// <param name="karma">The karma before the resurrection (it picks Lord British's words).</param>
        public ResurrectionScene(DataOvl ovl, byte[] karmaDat, int karma)
        {
            this.ovl = ovl;
            this.karmaDat = karmaDat;
            this.karma = karma;
        }

        /// <summary>Lord British's words for a karma: the KARMA.DAT paragraph at g_karma_text_offsets[karma / 20] (at most 2000 bytes, NUL-ended).</summary>
        public static string KarmaText(DataOvl ovl, byte[] karmaDat, int karma)
        {
            int off = ovl.U16(KarmaTexts + 2 * Math.Max(0, Math.Min(4, karma / 20)));
            int end = off;
            while (end < karmaDat.Length && end < off + 2000 && karmaDat[end] != 0) end++;
            return off < karmaDat.Length ? Encoding.ASCII.GetString(karmaDat, off, end - off) : "";
        }

        string S(int dg) => ovl.CStr(dg);

        void Blackout()
        {
            for (int y = 0; y < CutScene.Size; y++)
            for (int x = 0; x < CutScene.Size; x++)
                View.Poke(x, y, Darkness);
            View.ClearAll();
        }

        public IEnumerable<Beat> Run()
        {
            var v = View;
            yield return Beat.Wait(10);
            v.Print(S(SDarkness));
            v.Dark = true;
            yield return Beat.Wait(4);
            Blackout();
            v.Dark = false;
            v.Print(S(SRefuge));
            yield return Beat.Wait(0xe);
            v.Print(S(SNoEvil));
            yield return Beat.Wait(0x1c);
            v.Print(S(SDisturbed));
            v.Set(0, Sleeper, 5, 5);
            // the six sweeping noises
            yield return Beat.Wait(0x18);
            v.Print(S(SShouts));
            yield return Beat.Wait(6);
            foreach (var (x, light) in new[] { (2, LeftLight), (8, RightLight) })
            {
                v.Set(1, Sparkle, x, 7);
                yield return Beat.Wait(4);
                v.Poke(x, 7, light);
                v.Clear(1);
                yield return Beat.Wait(4);
            }
            v.Print(S(SThunder));
            // the earthquake's shaking, twice
            yield return Beat.Wait(0x10);
            v.Set(1, Sparkle, 5, 2);
            yield return Beat.Wait(4);
            v.Set(1, LordBritish, 5, 2);
            yield return Beat.Wait(4);
            v.Print(S(SQuote) + KarmaText(ovl, karmaDat, karma) + "\"");
            yield return Beat.Key;
            v.Print(S(SIntoned));
            yield return Beat.Wait(0x10);
            v.Print(S(SVertigo));
            yield return Beat.Wait(4);
            Blackout();
            v.Set(0, Sleeper, 5, 5);
            yield return Beat.Wait(0x14);
        }
    }
}
