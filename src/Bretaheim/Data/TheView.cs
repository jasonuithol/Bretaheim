using System;
using System.Collections.Generic;

namespace Bretaheim.Data
{
    /// <summary>What happens at one step of the View, for whoever plays it.</summary>
    public enum ViewEventKind
    {
        /// <summary>A new scene (<see cref="ViewEvent.Scene"/>): its caption and ground, no figures.</summary>
        Scene,
        /// <summary>Nothing new: let <see cref="ViewEvent.Frames"/> frames pass.</summary>
        Wait,
        /// <summary>A figure appears (<see cref="ViewEvent.Slot"/>).</summary>
        Place,
        /// <summary>A figure is gone.</summary>
        Remove,
        /// <summary>A figure steps one cell (from <see cref="ViewEvent.FromX"/>, <see cref="ViewEvent.FromY"/>).</summary>
        Move,
        /// <summary>A ground cell changes (<see cref="ViewEvent.X"/>, <see cref="ViewEvent.Y"/>, <see cref="ViewEvent.Tile"/>).</summary>
        Ground,
        /// <summary>The moongate opens at (X, Y) (the explosion frames, leaving tile 0xdc).</summary>
        GateOpens,
        /// <summary>The moongate at (X, Y) closes (the frames backwards, leaving grass).</summary>
        GateCloses,
        /// <summary>A figure dissolves in from sparkles.</summary>
        DissolveIn,
        /// <summary>A figure dissolves away into the ground.</summary>
        DissolveOut,
        /// <summary>A bolt from above strikes a figure (the Shadowlord's arrow at Shamino).</summary>
        Bolt,
    }

    public class ViewEvent
    {
        public ViewEventKind Kind;
        /// <summary>Frames of the original's 55 ms tick this step lasts.</summary>
        public int Frames;
        public int Scene = -1, Slot = -1, X, Y, FromX, FromY, Tile;
        /// <summary>Where in the script (offset in the 2000 bytes) its op is.</summary>
        public int At;
    }

    /// <summary>
    /// "Return to the View", the title's animated demo (FONT run_the_view): 2000 bytes of
    /// MISCMAPS.DAT from 0x2c0, four scenes of 4×19 tiles (rows 0x20 apart, 0x80 each) and a
    /// byte script from 0x200 moving figures (monster-table slots, TILES.16 tile = byte + 0x100)
    /// over them: 0 place, 1 remove, 2 step (13: step and 7 frames), 3 wait, 4/5 the moongate's
    /// explosion/implosion, 6 scene (caption from DGROUP 0x515c), 7/8 dissolve in/out, 9 restart,
    /// 10 ground, 11 lightning on a slot, 12 clear, 14/15 loop. A frame is a 55 ms tick.
    /// </summary>
    public class TheView
    {
        public const int Rows = 4, Columns = 19, Slots = 16, FileOffset = 0x2c0, Length = 2000, ScriptAt = 0x200;
        public const int Captions = 0x515c, DirX = 0x24d6, DirY = 0x24de;
        /// <summary>The gate's tile once open, and what it leaves when it closes.</summary>
        public const byte GateTile = 0xdc, Grass = 5;
        /// <summary>The 32 key polls of a 256-step dissolve, the explosion's 15 frames.</summary>
        public const int DissolveFrames = 32, GateFrames = 15;
        public const float Tick = 0.055f;

        public readonly string[] Caption = new string[4];
        readonly byte[] d;
        readonly int[] dx = new int[4], dy = new int[4];

        /// <summary>The scene now: ground [row, column] and figures.</summary>
        public readonly byte[,] Ground = new byte[Rows, Columns];
        public readonly byte[] Tile = new byte[Slots];
        public readonly int[] X = new int[Slots], Y = new int[Slots];
        public int Scene { get; private set; } = -1;

        public TheView(byte[] miscmaps, DataOvl ovl)
        {
            // (the original reads 2000 bytes; the file ends sooner, after the script's restart)
            if (miscmaps.Length < FileOffset + 0x48f) throw new U5FormatException($"MISCMAPS.DAT is {miscmaps.Length} bytes, too short for the View");
            d = new byte[Length];
            Array.Copy(miscmaps, FileOffset, d, 0, Math.Min(Length, miscmaps.Length - FileOffset));
            for (int i = 0; i < 4; i++)
            {
                Caption[i] = ovl.CStr(ovl.U16(Captions + i * 2)).Trim();
                dx[i] = ovl.I8(DirX + i * 2);
                dy[i] = ovl.I8(DirY + i * 2);
            }
        }

        /// <summary>Where a scene's strip lies on the 32×32 map it is staged on (<see cref="Stage"/>).</summary>
        public const int StageX = 6, StageY = 14, StageSize = 32;
        public const byte Nothing = 0xff, Tree = 0x0a, Wall = 0x4f, Cobble = 0x4d, Door = 0xb8;

        /// <summary>
        /// A scene on a 32×32 settlement-sized map (the strip at <see cref="StageX"/>, <see cref="StageY"/>)
        /// for building it in the world: the bedroom alone in nothing; out of doors, forest to the
        /// north (none on the strip's own edge, nor south of it, where the onlooker stands) and
        /// grass south; Iolo's hut closed by a cobble wall to the north and one to the south with
        /// its door under the doorway the script walks through (8, 4).
        /// </summary>
        public byte[] Stage(int scene)
        {
            var raw = new byte[StageSize * StageSize];
            var rng = new Random(1 + scene);
            for (int y = 0; y < StageSize; y++)
            for (int x = 0; x < StageSize; x++)
            {
                byte t = Nothing;
                if (scene > 0)
                {
                    int north = StageY - y, south = y - (StageY + Rows - 1);
                    if (north >= 2 && north <= 7) t = rng.NextDouble() < 0.18 + 0.08 * north ? Tree : (byte)5;
                    else if (north == 1 || (south >= 1 && south <= 3)) t = 5;
                    else if (north <= 0 && south <= 0 && (x < StageX || x >= StageX + Columns)) t = rng.NextDouble() < 0.5 ? Tree : (byte)5;
                }
                raw[y * StageSize + x] = t;
            }
            for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Columns; c++)
                raw[(StageY + r) * StageSize + StageX + c] = SceneTile(scene, r, c);
            if (scene == 3)
                for (int c = 5; c <= 13; c++)
                {
                    raw[(StageY - 1) * StageSize + StageX + c] = Cobble;
                    raw[(StageY + Rows) * StageSize + StageX + c] = c == 8 ? Door : Cobble;
                }
            return raw;
        }

        /// <summary>A scene's ground as the script first shows it (0xff: nothing there).</summary>
        public byte SceneTile(int scene, int row, int col) => d[scene * 0x80 + row * 0x20 + col];

        void LoadScene(int n)
        {
            Scene = n;
            for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Columns; c++)
                Ground[r, c] = SceneTile(n, r, c);
        }

        void Poke(int x, int y, byte tile)
        {
            if ((uint)x < Columns && (uint)y < Rows) Ground[y, x] = tile;
        }

        /// <summary>The script once through (it restarts in the original), the state updated before each event is yielded.</summary>
        public IEnumerable<ViewEvent> Run()
        {
            int e = ScriptAt, loopStart = 0, loopCount = 0, ex = 0, ey = 0;
            int B(int k) => e + k < Length ? d[e + k] : 0;
            while (e < Length)
            {
                int op = B(0), at = e;
                ViewEvent ev = null;
                switch (op)
                {
                    case 0:
                    {
                        int s = B(1) & 15;
                        Tile[s] = (byte)B(2); X[s] = B(3); Y[s] = B(4);
                        ev = new ViewEvent { Kind = ViewEventKind.Place, Slot = s, X = X[s], Y = Y[s], Tile = Tile[s] };
                        e += 4;
                        break;
                    }
                    case 1:
                    {
                        int s = B(1) & 15;
                        Tile[s] = 0;
                        ev = new ViewEvent { Kind = ViewEventKind.Remove, Slot = s, X = X[s], Y = Y[s] };
                        e += 1;
                        break;
                    }
                    case 2:
                    case 0xd:
                    {
                        int s = B(1) & 15, dir = B(2) & 3;
                        ev = new ViewEvent { Kind = ViewEventKind.Move, Slot = s, FromX = X[s], FromY = Y[s], Frames = op == 0xd ? 7 : 0 };
                        X[s] += dx[dir]; Y[s] += dy[dir];
                        ev.X = X[s]; ev.Y = Y[s]; ev.Tile = Tile[s];
                        e += 2;
                        break;
                    }
                    case 3:
                        ev = new ViewEvent { Kind = ViewEventKind.Wait, Frames = B(1) };
                        e += 1;
                        break;
                    case 4:
                    case 5:
                        if (op == 4) { ex = B(1); ey = B(2); e += 2; }
                        Poke(ex, ey, op == 4 ? GateTile : Grass);
                        ev = new ViewEvent { Kind = op == 4 ? ViewEventKind.GateOpens : ViewEventKind.GateCloses, X = ex, Y = ey, Frames = GateFrames + 2 };
                        break;
                    case 6:
                        LoadScene(B(1) & 3);
                        ev = new ViewEvent { Kind = ViewEventKind.Scene, Scene = Scene };
                        e += 1;
                        break;
                    case 7:
                    case 8:
                    {
                        int s = B(1) & 15;
                        ev = new ViewEvent { Kind = op == 7 ? ViewEventKind.DissolveIn : ViewEventKind.DissolveOut, Slot = s, X = X[s], Y = Y[s], Tile = Tile[s], Frames = DissolveFrames };
                        e += 1;
                        break;
                    }
                    case 9:
                        yield break;
                    case 10:
                        Poke(B(2), B(3), (byte)B(1));
                        ev = new ViewEvent { Kind = ViewEventKind.Ground, X = B(2), Y = B(3), Tile = B(1) };
                        e += 3;
                        break;
                    case 0xb:
                    {
                        int s = B(2) & 15;
                        ev = new ViewEvent { Kind = ViewEventKind.Bolt, Slot = s, X = X[s], Y = Y[s], Frames = 5 + 3 };
                        e += 2;
                        break;
                    }
                    case 0xc:
                        for (int i = 0; i < Slots; i++) Tile[i] = 0;
                        break;
                    case 0xe:
                        loopStart = e + 1;
                        loopCount = B(1);
                        e = loopStart;
                        break;
                    case 0xf:
                        if (--loopCount != 0) e = loopStart;
                        break;
                }
                if (ev != null) { ev.At = at; yield return ev; }
                e++;
            }
        }

        /// <summary>The scene as the original draws it: its 4 rows of 16-pixel tiles, figures over the ground, the caption under them.</summary>
        public Screen Render(TileSet tiles, FixedFont font)
        {
            var s = new Screen();
            const int top = 7 * 16;
            for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Columns; c++)
            {
                int t = Ground[r, c];
                if (t == 0xff || t == 0xfe) continue;
                DrawTile(s, tiles, t, c, top / 16 + r);
            }
            for (int i = 0; i < Slots; i++)
                if (Tile[i] != 0 && (uint)X[i] < Columns && (uint)Y[i] < Rows) DrawTile(s, tiles, Tile[i] + 0x100, X[i], top / 16 + Y[i]);
            if (Scene >= 0) font.Draw(s, Caption[Scene], (Screen.W - Caption[Scene].Length * 8) / 2, top + Rows * 16 + 8, 15);
            return s;
        }

        static void DrawTile(Screen s, TileSet tiles, int tile, int col, int row)
        {
            if (tile < 0 || tile >= TileSet.Count) return;
            for (int y = 0; y < TileSet.Size; y++)
            for (int x = 0; x < TileSet.Size; x++)
                s.Put(8 + col * TileSet.Size + x, row * TileSet.Size + y, tiles.Pixel(tile, x, y));
        }
    }
}
