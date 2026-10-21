using System;
using System.Collections.Generic;
using System.Text;

namespace Bretaheim.Data
{
    /// <summary>IBM.CH / RUNES.CH: the original's fixed 8×8 fonts (128 glyphs of 8 rows, 1 bit a pixel, the leftmost pixel the high bit).</summary>
    public class FixedFont
    {
        public const int Size = 8;
        readonly byte[] d;

        public FixedFont(byte[] raw)
        {
            if (raw.Length < 128 * Size) throw new U5FormatException($"font is {raw.Length} bytes, expected {128 * Size}");
            d = raw;
        }

        public void Draw(Screen s, byte c, int x, int y, int colour)
        {
            int g = (c & 0x7f) * Size;
            for (int r = 0; r < Size; r++)
            for (int b = 0; b < Size; b++)
                if ((d[g + r] & (0x80 >> b)) != 0) s.Put(x + b, y + r, colour);
        }

        public void Draw(Screen s, string text, int x, int y, int colour)
        {
            foreach (char ch in text)
            {
                Draw(s, (byte)ch, x, y, colour);
                x += Size;
            }
        }
    }

    /// <summary>What a cut-scene asks of whoever plays it.</summary>
    public enum BeatKind
    {
        /// <summary>Show the view and wait <see cref="Beat.Seconds"/> (a footstep with <see cref="Beat.Footstep"/>).</summary>
        Wait,
        /// <summary>Show the view and wait for a key.</summary>
        Key,
        /// <summary>Show the view and ask for a typed line (<see cref="Beat.Prompt"/>): the answer goes back in the scene's Answer.</summary>
        Ask,
        /// <summary>Show the view and wait for Y or N: the answer goes back in the scene's Yes.</summary>
        YesNo,
        /// <summary>Show <see cref="Beat.Picture"/> instead of the view (an end-story screen) and wait for a key.</summary>
        Picture,
    }

    public class Beat
    {
        public BeatKind Kind;
        public float Seconds;
        public bool Footstep;
        /// <summary>Nothing more will happen: a key ends the scene here (the figures wander for ever in the original).</summary>
        public bool Endless;
        public string Prompt;
        public Screen Picture;
        public string Words;

        public static Beat Wait(int ticks, bool footstep = false) => new Beat { Kind = BeatKind.Wait, Seconds = ticks * Tick, Footstep = footstep };
        public static Beat Step => new Beat { Kind = BeatKind.Wait, Seconds = 5 * Tick, Footstep = true };
        public static Beat Key => new Beat { Kind = BeatKind.Key };
        /// <summary>One of the original's animate_wait / wait_ticks units, in seconds.</summary>
        public const float Tick = 0.06f;
    }

    /// <summary>
    /// The original's cut-scenes play in its 11×11 map window (BLCKTHRN, ENDGAME): a room of
    /// MISCMAPS.DAT tiles (g_light_map, poked as the scene goes), figures in monster-table slots
    /// (tile byte + 0x100 of TILES.16, at x, y), and the text window beside it, 16 IBM.CH
    /// characters wide, word-wrapped as print_string does. Rendered onto a 320×200 screen: the
    /// window at the left, the text in the column to its right, its latest lines.
    /// </summary>
    public class CutScene
    {
        public const int Size = 11, Slots = 32, Columns = 16, Rows = 23, MapX = 8, MapY = 12, TextX = 192, TextY = 8;
        /// <summary>The room's tiles, [y, x].</summary>
        public readonly byte[,] Map = new byte[Size, Size];
        /// <summary>Monster-table slots: tile byte (0: empty), x, y.</summary>
        public readonly byte[] Tile = new byte[Slots];
        public readonly int[] X = new int[Slots], Y = new int[Slots];
        /// <summary>The map window is black (blindfolded, dragged away).</summary>
        public bool Dark;
        /// <summary>g_moongate_height 0..16: the gate of the map tile <see cref="GateTile"/> rises out of the floor at (GateX, GateY).</summary>
        public int GateHeight, GateX, GateY, GateTile = 0xdc;
        readonly List<string> lines = new List<string>();
        readonly StringBuilder current = new StringBuilder();
        readonly StringBuilder all = new StringBuilder();

        /// <summary>Everything printed so far (for logs and tests).</summary>
        public string Text => all.ToString();
        /// <summary>The text window's lines, the line being written last.</summary>
        public List<string> Lines
        {
            get
            {
                var r = new List<string>(lines);
                r.Add(current.ToString());
                return r;
            }
        }

        /// <summary>A room of MISCMAPS.DAT: 11 rows of 16 bytes from <paramref name="offset"/>, 11 used.</summary>
        public void LoadMap(byte[] miscmaps, int offset)
        {
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                Map[y, x] = miscmaps[offset + y * 16 + x];
            Dark = false;
        }

        public void Poke(int x, int y, byte tile)
        {
            if ((uint)x < Size && (uint)y < Size) Map[y, x] = tile;
        }

        /// <summary>set_monster_slot: a figure (tile byte; 0 clears the slot) at (x, y).</summary>
        public void Set(int slot, byte tile, int x, int y)
        {
            Tile[slot] = tile;
            X[slot] = x;
            Y[slot] = y;
        }

        public void Clear(int slot) => Tile[slot] = 0;

        public void ClearAll()
        {
            for (int i = 0; i < Slots; i++) Tile[i] = 0;
        }

        /// <summary>print_string into the text window: word-wrapped at 16 columns, '\n' a new line.</summary>
        public void Print(string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            all.Append(s);
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '\r') { i++; continue; }
                if (c == '\n') { NewLine(); i++; continue; }
                if (c == ' ')
                {
                    if (current.Length >= Columns) NewLine();
                    else if (current.Length > 0) current.Append(' ');
                    i++;
                    continue;
                }
                int end = i;
                while (end < s.Length && s[end] != ' ' && s[end] != '\n' && s[end] != '\r') end++;
                var word = s.Substring(i, end - i);
                if (current.Length > 0 && current.Length + word.Length > Columns) NewLine();
                while (word.Length > Columns)
                {
                    current.Append(word, 0, Columns);
                    word = word.Substring(Columns);
                    NewLine();
                }
                current.Append(word);
                i = end;
            }
        }

        void NewLine()
        {
            lines.Add(current.ToString().TrimEnd());
            current.Clear();
            if (lines.Count > 200) lines.RemoveRange(0, lines.Count - 200);
        }

        /// <summary>The scene as the original's screen shows it: the map window and the text beside it.</summary>
        public Screen Render(TileSet tiles, FixedFont font)
        {
            var s = new Screen();
            if (!Dark)
            {
                for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    int tile = Map[y, x];
                    if (GateHeight > 0 && x == GateX && y == GateY)
                    {
                        DrawTile(s, tiles, tile, x, y, 0);
                        DrawTile(s, tiles, GateTile, x, y, TileSet.Size - Math.Min(GateHeight, TileSet.Size));
                    }
                    else DrawTile(s, tiles, tile, x, y, 0);
                }
                for (int i = 0; i < Slots; i++)
                    if (Tile[i] != 0 && (uint)X[i] < Size && (uint)Y[i] < Size) DrawTile(s, tiles, Tile[i] + 0x100, X[i], Y[i], 0);
            }
            var shown = Lines;
            int first = Math.Max(0, shown.Count - Rows);
            for (int r = first; r < shown.Count; r++)
                font.Draw(s, shown[r], TextX, TextY + (r - first) * FixedFont.Size, 15);
            return s;
        }

        /// <summary>A 16×16 tile in a map-window cell, its rows from <paramref name="fromRow"/> down.</summary>
        static void DrawTile(Screen s, TileSet tiles, int tile, int cx, int cy, int fromRow)
        {
            if (tile < 0 || tile >= TileSet.Count) return;
            for (int y = fromRow; y < TileSet.Size; y++)
            for (int x = 0; x < TileSet.Size; x++)
                s.Put(MapX + cx * TileSet.Size + x, MapY + cy * TileSet.Size + y, tiles.Pixel(tile, x, y));
        }

        /// <summary>
        /// BLCKTHRN run_move_script: byte code at a DGROUP address moving the scene's figures. Below
        /// 0x10: 0 end, 1 pair (the next move code takes a second code for another slot), 2 n
        /// count, 3/4 footsteps on/off, 5 n wait, 6 v x y poke the room, 7 redraw, 8 footsteps
        /// (count), 9 s clear a slot. From 0x10 a move: slot by the high bits (0x10 → 6, 0x14 → 7,
        /// 0x18 → 8, 0x1c → 0, 0x20 → 1), direction by the low two (N, E, S, W), count times.
        /// </summary>
        public IEnumerable<Beat> RunScript(DataOvl ovl, int at)
        {
            bool steps = true;
            while (true)
            {
                int count = 1;
                bool pair = false;
                byte code;
                while (true)
                {
                    byte b = ovl.U8(at);
                    if (b == 0) yield break;
                    if (b >= 0x10) { code = b; at++; break; }
                    switch (b)
                    {
                        case 1: pair = true; at++; break;
                        case 2: count = ovl.U8(at + 1); at += 2; break;
                        case 3: steps = true; at++; break;
                        case 4: steps = false; at++; break;
                        case 5: yield return Beat.Wait(ovl.U8(at + 1)); at += 2; break;
                        case 6: Poke(ovl.U8(at + 2), ovl.U8(at + 3), ovl.U8(at + 1)); at += 4; break;
                        case 7: yield return Beat.Wait(0); at++; break;
                        case 8:
                            for (int k = 0; k < count; k++) yield return Beat.Step;
                            count = 1;
                            at++;
                            break;
                        case 9: Clear(ovl.U8(at + 1)); at += 2; break;
                        default: at++; break;
                    }
                }
                var (slot, dx, dy) = Move(code);
                int slot2 = 0, dx2 = 0, dy2 = 0;
                if (pair) { (slot2, dx2, dy2) = Move(ovl.U8(at)); at++; }
                for (int k = 0; k < count; k++)
                {
                    X[slot] += dx; Y[slot] += dy;
                    if (pair) { X[slot2] += dx2; Y[slot2] += dy2; }
                    yield return steps ? Beat.Step : Beat.Wait(0);
                }
            }
        }

        /// <summary>decode_move_code: (slot, dx, dy) of a move byte (an unknown slot is 0, as the port has it).</summary>
        public static (int slot, int dx, int dy) Move(int code)
        {
            int hi = code & 0xfc;
            int slot = hi == 0x10 ? 6 : hi == 0x14 ? 7 : hi == 0x18 ? 8 : hi == 0x1c ? 0 : hi == 0x20 ? 1 : 0;
            switch (code & 3)
            {
                case 0: return (slot, 0, -1);
                case 1: return (slot, 1, 0);
                case 2: return (slot, 0, 1);
                default: return (slot, -1, 0);
            }
        }

        /// <summary>ENDGAME move_slot_toward: one step of a figure toward (x, y), along y when it is further off that way; false when it is there (or empty).</summary>
        public bool StepToward(int slot, int x, int y)
        {
            if (Tile[slot] == 0 || (X[slot] == x && Y[slot] == y)) return false;
            if (Math.Abs(X[slot] - x) < Math.Abs(Y[slot] - y)) Y[slot] += y < Y[slot] ? -1 : 1;
            else X[slot] += x < X[slot] ? -1 : 1;
            return true;
        }

        /// <summary>A figure walking to (x, y), a step at a time (end_step_pause).</summary>
        public IEnumerable<Beat> Walk(int slot, int x, int y)
        {
            while (StepToward(slot, x, y)) yield return Beat.Step;
        }

        /// <summary>The NUL-ended string at a DGROUP address of a file the original loads at <paramref name="loadedAt"/> (MISCMSG.DAT, ENDMSG.DAT at 0xb21e).</summary>
        public static string StrAt(byte[] file, int dg, int loadedAt = 0xb21e)
        {
            int i = dg - loadedAt, end = i;
            while (end < file.Length && file[end] != 0) end++;
            return Encoding.ASCII.GetString(file, i, end - i);
        }
    }
}
