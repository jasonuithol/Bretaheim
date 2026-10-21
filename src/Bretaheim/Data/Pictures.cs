using System;
using System.IO;
using System.Text;

namespace Bretaheim.Data
{
    /// <summary>One picture of a *.16 file: EGA colour indices row by row from the top, and where it is see-through.</summary>
    public class U5Image
    {
        public int Width, Height;
        public byte[] Pixels;
        /// <summary>True where the picture is not drawn (its 1-bpp mask), or null.</summary>
        public bool[] Clear;
    }

    /// <summary>
    /// A multi-picture *.16 file (STORYn.16, TEXT.16, CREATE.16 …): LZW, then u16 count and per
    /// picture a u16 offset and a u16 mask offset; each picture u16 width, u16 height and rows of
    /// two pixels a byte (high nibble first, rows padded to 8 pixels); a mask is u16 w, u16 h and
    /// 1-bpp rows, a set bit not drawn (Ultima5Revolutions data/images.rs, platform/video.rs draw_image).
    /// </summary>
    public class PictureFile
    {
        public readonly U5Image[] Images;

        public PictureFile(byte[] raw) : this(raw, true) { }

        public PictureFile(byte[] data, bool compressed)
        {
            var d = compressed ? Lzw.DecompressFile(data) : data;
            int count = U16(d, 0);
            Images = new U5Image[count];
            for (int i = 0; i < count; i++)
            {
                int off = U16(d, 2 + 4 * i), maskOff = U16(d, 4 + 4 * i);
                if (off == 0 || off + 4 > d.Length) continue;
                int w = U16(d, off), h = U16(d, off + 2), rb = (w + 7) / 8 * 4;
                if (w == 0 || h == 0 || off + 4 + rb * h > d.Length) continue;
                var img = new U5Image { Width = w, Height = h, Pixels = new byte[w * h] };
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    byte b = d[off + 4 + y * rb + x / 2];
                    img.Pixels[y * w + x] = (byte)(x % 2 == 0 ? b >> 4 : b & 15);
                }
                if (maskOff != 0 && maskOff + 4 <= d.Length)
                {
                    int mb = (w + 7) / 8;
                    img.Clear = new bool[w * h];
                    for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int p = maskOff + 4 + y * mb + x / 8;
                        img.Clear[y * w + x] = p < d.Length && (d[p] & (0x80 >> (x & 7))) != 0;
                    }
                }
                Images[i] = img;
            }
        }

        public U5Image this[int i] => i >= 0 && i < Images.Length ? Images[i] : null;

        static int U16(byte[] d, int o) => o + 1 < d.Length ? d[o] | d[o + 1] << 8 : 0;
    }

    /// <summary>
    /// PROPORT.PCS, the proportional font of the story and the gypsy: LZW, then u16 count, u16
    /// offsets, each glyph u16 width, u16 height and 1-bpp rows (glyph = character − 0x20); the
    /// advance of each character is DATA.OVL's width table (DGROUP 0x50ca) + 1.
    /// </summary>
    public class ProportionalFont
    {
        public const int WidthTable = 0x50ca, SpaceWidth = 0x5154, HyphenGlyph = 0xd;
        readonly byte[] d;
        readonly DataOvl ovl;

        public ProportionalFont(byte[] raw, DataOvl ovl)
        {
            d = Lzw.DecompressFile(raw);
            this.ovl = ovl;
        }

        public int Count => d[0] | d[1] << 8;
        public int Width(byte c) => ovl.U8(WidthTable + c);
        public int DefaultSpace => ovl.U8(SpaceWidth);

        /// <summary>Draws glyph <paramref name="index"/> in white (15) with its top left at (x, y) (draw_bitmap_white).</summary>
        public void Draw(Screen s, int index, int x, int y)
        {
            if (index < 0 || index >= Count) return;
            int off = d[2 + 2 * index] | d[3 + 2 * index] << 8;
            int w = d[off] | d[off + 1] << 8, h = d[off + 2] | d[off + 3] << 8, rb = (w + 7) >> 3;
            for (int r = 0; r < h; r++)
            for (int px = 0; px < rb * 8; px++)
                if ((d[off + 4 + r * rb + px / 8] & (0x80 >> (px & 7))) != 0) s.Put(x + px, y + r, 15);
        }
    }

    /// <summary>
    /// The intro's text layout block (DGROUP 0x5146..0x5158): two column bands, band 1 while
    /// <see cref="Band1From"/> &lt; y &lt; <see cref="Band1To"/>; the cursor; the space width.
    /// </summary>
    public class TextLayout
    {
        public int Left0, Left1, Right0 = 320, Right1 = 320, Band1From = 200, Band1To = 200, X, Y, Space = 5;
        int Band(int y) => Band1From < y && y < Band1To ? 1 : 0;
        public int Left(int y) => Band(y) == 1 ? Left1 : Left0;
        public int Right(int y) => Band(y) == 1 ? Right1 : Right0;
    }

    /// <summary>A 320×200 EGA screen (colour indices) the intro and the gypsy are drawn on.</summary>
    public class Screen
    {
        public const int W = 320, H = 200;
        public readonly byte[] Pixels = new byte[W * H];

        public void Put(int x, int y, int c)
        {
            if ((uint)x < W && (uint)y < H) Pixels[y * W + x] = (byte)c;
        }

        /// <summary>A picture with its left edge on a byte boundary (x &amp; ~7), its mask honoured (draw_image on page 1).</summary>
        public void Draw(U5Image img, int x, int y)
        {
            if (img == null) return;
            int x0 = x & ~7;
            for (int r = 0; r < img.Height; r++)
            for (int c = 0; c < img.Width; c++)
            {
                int i = r * img.Width + c;
                if (img.Clear != null && img.Clear[i]) continue;
                Put(x0 + c, y + r, img.Pixels[i]);
            }
        }

        /// <summary>
        /// FONT draw_proportional_text: the NUL-ended text word-wrapped in the layout's bands, full
        /// justified but for a paragraph's last line, '{' a 15-pixel gap, '_' a soft hyphen (a '-'
        /// drawn where a line breaks there), lines 9 pixels apart, nothing drawn from y 0xc0. The
        /// layout's cursor is left after the text.
        /// </summary>
        public void Text(byte[] text, ProportionalFont font, TextLayout l)
        {
            byte At(int i) => i < text.Length ? text[i] : (byte)0;
            int width = l.Right(l.Y) - l.Left(l.Y);
            int pos = 0, cur = l.X - l.Left(l.Y);
            while (true)
            {
                if (At(pos) == 0) return;
                int spaces = 0, i = pos;
                while (At(i) != 0 && cur < width && At(i) != '\n')
                {
                    byte c = At(i);
                    if (c < 0x21) { cur += l.Space; spaces++; }
                    else if (c == '{') cur += 15;
                    else if (c != '_') cur += font.Width(c) + 1;
                    i++;
                }
                bool hyphen = false;
                while (true)
                {
                    byte c = At(i);
                    if ((c == 0 && cur < width) || c == '\n') break;
                    i--;
                    if (i <= pos) break;
                    c = At(i);
                    if (c == ' ') { cur -= l.Space; spaces--; break; }
                    if (c == '_' && font.Width((byte)'-') + cur + 1 < width) { hyphen = true; break; }
                    if (c != '_' && c != '{') cur -= font.Width(c) + 1;
                }
                if (hyphen) cur += font.Width((byte)'-') + 1;
                int slack = width - cur;
                if (pos < i)
                {
                    byte brk = At(i);
                    do
                    {
                        byte c = At(pos);
                        if (c < 0x21)
                        {
                            l.X += l.Space;
                            if (spaces != 0 && brk != 0 && brk != '\n')
                            {
                                l.X += slack / spaces;
                                slack -= slack / spaces;
                                spaces--;
                            }
                        }
                        else if (c == '{') l.X += 15;
                        else if (c != '_')
                        {
                            if (l.Y < 0xc0) font.Draw(this, c - 0x20, l.X, l.Y);
                            l.X += font.Width(c) + 1;
                        }
                        pos++;
                    } while (i != pos);
                }
                pos = i;
                if (At(i) != 0)
                {
                    if (At(i) == '_' && l.Y < 0xc0) font.Draw(this, ProportionalFont.HyphenGlyph, l.X, l.Y);
                    cur = 0;
                    l.Y += 9;
                    l.X = l.Left(l.Y);
                    width = l.Right(l.Y) - l.X;
                    pos = i + 1;
                }
            }
        }

        /// <summary>RGBA bytes, rows from the top, in the standard EGA palette.</summary>
        public byte[] Rgba()
        {
            var r = new byte[W * H * 4];
            for (int i = 0; i < Pixels.Length; i++)
            {
                int c = TileSet.EgaPalette[Pixels[i] & 15];
                r[i * 4] = (byte)(c >> 16); r[i * 4 + 1] = (byte)(c >> 8); r[i * 4 + 2] = (byte)c; r[i * 4 + 3] = 255;
            }
            return r;
        }

        /// <summary>A binary PPM of the screen (for U5Dump).</summary>
        public void WritePpm(string path)
        {
            using (var f = File.Create(path))
            {
                var head = Encoding.ASCII.GetBytes($"P6\n{W} {H}\n255\n");
                f.Write(head, 0, head.Length);
                var rgba = Rgba();
                for (int i = 0; i < W * H; i++) f.Write(rgba, i * 4, 3);
            }
        }

        /// <summary>The NUL-ended text at an offset of a file (the original reads up to 2000 bytes).</summary>
        public static byte[] TextAt(byte[] file, int offset)
        {
            int end = offset;
            while (end < file.Length && end - offset < 2000 && file[end] != 0) end++;
            var r = new byte[end - offset];
            Array.Copy(file, offset, r, 0, r.Length);
            return r;
        }

        /// <summary>The text as plain words: soft hyphens and indents gone.</summary>
        public static string Plain(byte[] text) => Encoding.ASCII.GetString(text).Replace("_", "").Replace("{", "");
    }
}
