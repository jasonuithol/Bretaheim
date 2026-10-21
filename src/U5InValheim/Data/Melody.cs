namespace U5InValheim.Data
{
    /// <summary>
    /// The harpsichord's tune (TOWN handle_number_key): a digit pressed at a harpsichord plays a
    /// note and moves the matcher along the 13-note melody (DATA.OVL 0x275a: 6 7 8 9 8 7 8 7 6 7 6
    /// 5 3). The whole melody played opens (or closes again) the secret passage to the Sandalwood
    /// Box on the third floor of Lord British's castle.
    /// </summary>
    public class Melody
    {
        const int Notes = 0x275a, Length = 13, Frequencies = 0x2746;
        readonly byte[] tune = new byte[Length];
        int pos;

        public Melody(DataOvl ovl)
        {
            for (int i = 0; i < Length; i++) tune[i] = ovl.U8(Notes + i);
        }

        /// <summary>The melody's notes (digits).</summary>
        public string Tune => string.Concat(System.Array.ConvertAll(tune, n => n.ToString()));

        /// <summary>The original's value for digit n's note (0..9; the scale 1..9 rises from it, the frequency is in proportion).</summary>
        public static int Frequency(DataOvl ovl, int n) => ovl.U16(Frequencies + 2 * n);

        /// <summary>Plays digit n; true when it completes the melody (which starts again). A wrong note falls back as the original's does.</summary>
        public bool Play(int n)
        {
            if (tune[pos] == n)
            {
                if (++pos < Length) return false;
                pos = 0;
                return true;
            }
            if (pos == 10 && n == 8) pos = 3;
            else if (pos == 11 && n == 7) pos = 2;
            else pos = n == tune[0] ? 1 : 0;
            return false;
        }
    }
}
