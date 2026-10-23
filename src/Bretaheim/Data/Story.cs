using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Bretaheim.Data
{
    /// <summary>One screen of the introduction: what is shown (and, for some, a second picture appearing after), and its words.</summary>
    public class StoryScreen
    {
        public Screen Picture;
        /// <summary>The same screen once its extra picture has appeared (hook 2: the dissolve), or null.</summary>
        public Screen After;
        public string Text;
        /// <summary>The screen's pictures alone (no headings, no words; the second picture too), and the rectangle they fill.</summary>
        public Screen Art;
        public int ArtX, ArtY, ArtW, ArtH;
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

        /// <summary>
        /// A paragraph in pages to read one at a time under a scene: its own lines kept apart, a
        /// long one broken between sentences (never inside a quotation's sentence), each page at
        /// most about <paramref name="most"/> words unless one sentence is longer.
        /// </summary>
        public static List<string> Pages(string text, int most = 45)
        {
            var pages = new List<string>();
            foreach (var line in text.Split('\n'))
            {
                var sentences = new List<string>();
                var current = new StringBuilder();
                for (int i = 0; i < line.Length; i++)
                {
                    current.Append(line[i]);
                    // a sentence ends at . ! ? (and any closing quote after it) before a space
                    if ((line[i] == '.' || line[i] == '!' || line[i] == '?'))
                    {
                        while (i + 1 < line.Length && (line[i + 1] == '"' || line[i + 1] == '\'')) current.Append(line[++i]);
                        if (i + 1 >= line.Length || line[i + 1] == ' ')
                        {
                            sentences.Add(current.ToString().Trim());
                            current.Clear();
                        }
                    }
                }
                if (current.ToString().Trim().Length > 0) sentences.Add(current.ToString().Trim());
                var page = new StringBuilder();
                int words = 0;
                foreach (var sentence in sentences)
                {
                    int n = Words(sentence);
                    if (words > 0 && words + n > most)
                    {
                        pages.Add(page.ToString());
                        page.Clear();
                        words = 0;
                    }
                    if (page.Length > 0) page.Append(' ');
                    page.Append(sentence);
                    words += n;
                }
                if (page.Length > 0) pages.Add(page.ToString());
            }
            return pages;
        }

        public static int Words(string text) => text.Split(new[] { ' ', '\n' }, System.StringSplitOptions.RemoveEmptyEntries).Length;

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
                var art = new Screen();
                int ax0 = Screen.W, ay0 = Screen.H, ax1 = 0, ay1 = 0;
                void Picture(U5Image img, int x, int y)
                {
                    if (img == null) return;
                    screen.Draw(img, x, y);
                    art.Draw(img, x, y);
                    ax0 = System.Math.Min(ax0, x & ~7); ay0 = System.Math.Min(ay0, y);
                    ax1 = System.Math.Max(ax1, (x & ~7) + img.Width); ay1 = System.Math.Max(ay1, y + img.Height);
                }
                int px = ovl.U8(Dg.StoryPicX + s), py = ovl.U8(Dg.StoryPicY + s);
                Picture(story[ovl.U8(Dg.StoryImageIndex + s)], px, py);
                if (hook > 3) Picture(story[hook * 2 - 5], px, py + TextBelow);
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
                    Picture(story[3], 0x60, 0x27);
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
                var shown = new StoryScreen { Picture = screen, Text = words.ToString().Trim(), Art = art };
                if (hook == 2)
                {
                    shown.After = new Screen();
                    System.Array.Copy(screen.Pixels, shown.After.Pixels, screen.Pixels.Length);
                    shown.After.Draw(story[2], 0x28, 0x56);
                    art.Draw(story[2], 0x28, 0x56);
                }
                ax1 = System.Math.Min(ax1, Screen.W); ay1 = System.Math.Min(ay1, Screen.H);
                if (ax1 > ax0 && ay1 > ay0) { shown.ArtX = ax0; shown.ArtY = ay0; shown.ArtW = ax1 - ax0; shown.ArtH = ay1 - ay0; }
                r.Add(shown);
            }
            return r;
        }
    }
}
