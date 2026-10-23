using System;

namespace Bretaheim.Data
{
    /// <summary>
    /// Iolo's lute, heard through his door as the friends come to it: Greensleeves (an old ballad,
    /// free to play) plucked on synthesised strings (Karplus-Strong: a burst of noise in a delay
    /// line the length of the note's period, averaged each pass, rings and mellows like a string),
    /// the tune over a picked bass, in 3/4.
    /// </summary>
    public static class Lute
    {
        public const int Rate = 44100;
        /// <summary>One beat, s.</summary>
        public const float Beat = 0.5f;

        // (MIDI note, beats): the pickup, then sixteen bars
        static readonly (int note, float beats)[] Tune =
        {
            (69, 1),
            (72, 2), (74, 1), (76, 1.5f), (77, 0.5f), (76, 1), (74, 2), (71, 1), (67, 1.5f), (69, 0.5f), (71, 1),
            (72, 2), (69, 1), (69, 1.5f), (68, 0.5f), (69, 1), (71, 2), (68, 1), (64, 2), (69, 1),
            (72, 2), (74, 1), (76, 1.5f), (77, 0.5f), (76, 1), (74, 2), (71, 1), (67, 1.5f), (69, 0.5f), (71, 1),
            (72, 1.5f), (71, 0.5f), (69, 1), (68, 1.5f), (66, 0.5f), (68, 1), (69, 3), (69, 3),
        };

        // each bar's chord, picked root, fifth, octave
        static readonly int[][] Chords =
        {
            Am, C, G, Em, Am, E, E, Am,
            Am, C, G, Em, Am, E, Am, Am,
        };
        static int[] Am => new[] { 45, 52, 57 };
        static int[] C => new[] { 48, 55, 60 };
        static int[] G => new[] { 43, 50, 55 };
        static int[] Em => new[] { 40, 47, 52 };
        static int[] E => new[] { 40, 47, 56 };

        /// <summary>The tune once through with its last notes ringing out, mono samples at <see cref="Rate"/> (peak 0.8).</summary>
        public static float[] Play()
        {
            var random = new Random(1580);
            float total = (1 + Chords.Length * 3) * Beat + 2.5f;
            var buffer = new float[(int)(total * Rate)];
            float t = 0f;
            foreach (var (note, beats) in Tune)
            {
                Pluck(buffer, t, note, 0.55f, 0.6f, random);
                t += beats * Beat;
            }
            for (int bar = 0; bar < Chords.Length; bar++)
                for (int k = 0; k < 3; k++)
                    Pluck(buffer, (1 + bar * 3 + k) * Beat, Chords[bar][k], k == 0 ? 0.4f : 0.25f, 0.35f, random);
            float peak = 0f;
            foreach (float v in buffer) peak = Math.Max(peak, Math.Abs(v));
            if (peak > 0f) for (int i = 0; i < buffer.Length; i++) buffer[i] *= 0.8f / peak;
            return buffer;
        }

        /// <summary>One string plucked at <paramref name="at"/> s, added in (gut strings: soft, a quick fade).</summary>
        static void Pluck(float[] into, float at, int note, float volume, float brightness, Random random)
        {
            double freq = 440.0 * Math.Pow(2.0, (note - 69) / 12.0);
            int period = Math.Max(2, (int)Math.Round(Rate / freq));
            var line = new float[period];
            // the pluck: noise, smoothed (a finger, not a pick)
            float last = 0f;
            for (int i = 0; i < period; i++)
            {
                float n = (float)(random.NextDouble() * 2.0 - 1.0);
                last = last + brightness * (n - last);
                line[i] = last;
            }
            int start = (int)(at * Rate), length = Math.Min(into.Length - start, (int)(2.2f * Rate));
            // each pass along the string loses a little: dying away (to a thousandth) in 1.8 s low, 1 s high
            double ring = Math.Max(1.0, 1.8 - 0.0012 * freq);
            float loss = (float)Math.Exp(Math.Log(0.001) / (freq * ring));
            for (int i = 0, j = 0; i < length; i++)
            {
                int next = (j + 1) % period;
                float v = line[j];
                line[j] = loss * 0.5f * (line[j] + line[next]);
                j = next;
                // a short attack, and a fade at the end of its time
                float env = Math.Min(1f, i / 40f) * Math.Min(1f, (length - i) / (0.2f * Rate));
                into[start + i] += v * volume * env;
            }
        }

        /// <summary>The tune's length, s (its last note struck).</summary>
        public static float Length
        {
            get
            {
                float t = 0f;
                foreach (var (_, beats) in Tune) t += beats * Beat;
                return t;
            }
        }
    }
}
