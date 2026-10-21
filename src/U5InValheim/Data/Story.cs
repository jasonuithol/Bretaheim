using System.Collections.Generic;
using System.IO;
using System.Text;

namespace U5InValheim.Data
{
    /// <summary>One screen of the introduction: what is shown (and, for some, a second picture appearing after), and its words.</summary>
    public class StoryScreen
    {
        public Screen Picture;
        /// <summary>The same screen once its extra picture has appeared (hook 2: the dissolve), or null.</summary>
        public Screen After;
        public string Text;
    }

    /// <summary>
    /// "Ultima V Introduction": INTRO play_story_sequence's 21 screens, drawn as the original draws
    /// them (Rust port ovl/intro.rs) from the player's files: each screen's picture of STORYn.16
    /// (DATA.OVL g_story_* tables: file, picture, x, y), the headings of TEXT.16 on screens 0, 7 and
    /// 14, a second picture for hooks above 3, and its paragraph of STORY.DAT in the proportional
    /// font laid out in the screen's two bands; screen 6 (hook 3) is the blue door's two lines.
    /// </summary>
    public static class Story
    {
        public const int Screens = 21;
        const int TextBelow = 0x37;

        /// <summary>Whether the files the intro needs are in the Ultima V folder.</summary>
        public static bool Available(string dir)
        {
            foreach (var f in new[] { "PROPORT.PCS", "TEXT.16", "STORY.DAT", "STORY1.16", "STORY6.16" })
                if (U5Data.FindFile(dir, f) == null) return false;
            return true;
        }

        public static List<StoryScreen> Load(string dir, DataOvl ovl)
        {
            byte[] Read(string name) => File.ReadAllBytes(U5Data.FindFile(dir, name));
            var font = new ProportionalFont(Read("PROPORT.PCS"), ovl);
            var text = new PictureFile(Read("TEXT.16"));
            var storyDat = Read("STORY.DAT");
            var files = new Dictionary<int, PictureFile>();
            var r = new List<StoryScreen>();
            for (int s = 0; s < Screens; s++)
            {
                int number = ovl.U8(Dg.StoryNumber + s), hook = ovl.U8(Dg.StoryHook + s);
                if (!files.TryGetValue(number, out var story)) files[number] = story = new PictureFile(Read($"STORY{number + 1}.16"));
                var screen = new Screen();
                if (hook == 1)
                {
                    // the chapter headings
                    if (s == 0) { screen.Draw(text[0], 0xe0, 0x1e); screen.Draw(text[1], 0xa8, 0x3a); }
                    else if (s == 7) { screen.Draw(text[0], 0xe8, 0x1a); screen.Draw(text[2], 200, 0x36); }
                    else if (s == 14) { screen.Draw(text[0], 0xb8, 0); screen.Draw(text[3], 0xf8, 0); }
                }
                int px = ovl.U8(Dg.StoryPicX + s), py = ovl.U8(Dg.StoryPicY + s);
                screen.Draw(story[ovl.U8(Dg.StoryImageIndex + s)], px, py);
                if (hook > 3) screen.Draw(story[hook * 2 - 5], px, py + TextBelow);
                var layout = new TextLayout
                {
                    Left0 = ovl.U8(Dg.StoryLeftX + s * 2), Left1 = ovl.U8(Dg.StoryLeftX + s * 2 + 1),
                    Right0 = ovl.U16(Dg.StoryRightX + s * 4), Right1 = ovl.U16(Dg.StoryRightX + s * 4 + 2),
                    Band1From = ovl.U8(Dg.StoryPara1Y + s), Band1To = ovl.U8(Dg.StoryPara2Y + s),
                    X = ovl.U8(Dg.StoryFirstLineX + s), Y = ovl.U8(Dg.StoryTextY + s), Space = font.DefaultSpace,
                };
                var words = new StringBuilder();
                if (hook == 3)
                {
                    // the blue door
                    screen.Draw(story[3], 0x60, 0x27);
                    var first = Encoding.ASCII.GetBytes(ovl.CStr(Dg.StrMoongateEnter));
                    screen.Text(first, font, layout);
                    layout.Y = 0xb4;
                    layout.X = ovl.U8(Dg.StoryFirstLineX + s);
                    var second = Encoding.ASCII.GetBytes(ovl.CStr(Dg.StrMoongateEnter + first.Length + 1));
                    screen.Text(second, font, layout);
                    words.Append(Screen.Plain(first)).Append(' ').Append(Screen.Plain(second));
                }
                else
                {
                    var para = Screen.TextAt(storyDat, ovl.U16(Dg.StoryTextOffsets + s * 2));
                    screen.Text(para, font, layout);
                    words.Append(Screen.Plain(para));
                }
                var shown = new StoryScreen { Picture = screen, Text = words.ToString().Trim() };
                if (hook == 2)
                {
                    shown.After = new Screen();
                    System.Array.Copy(screen.Pixels, shown.After.Pixels, screen.Pixels.Length);
                    shown.After.Draw(story[2], 0x28, 0x56);
                }
                r.Add(shown);
            }
            return r;
        }
    }
}
