using System;
using System.Collections.Generic;

namespace Bretaheim.Data
{
    /// <summary>
    /// LZW decompressor for the *.16, *.4, *.BIT and PROPORT.PCS files. Port of
    /// Ultima5Revolutions rust/src/lzw.rs: file = u32 uncompressed length + code stream;
    /// variable code width 9..12 bits, LSB-first, CLEAR = 256, END = 257.
    /// </summary>
    public static class Lzw
    {
        const int Clear = 256;
        const int End = 257;

        public static byte[] DecompressFile(byte[] raw)
        {
            if (raw.Length < 4) throw new U5FormatException("LZW file too short");
            int expected = BitConverter.ToInt32(raw, 0);
            var output = Decompress(raw, 4);
            if (output.Length != expected)
                throw new U5FormatException($"LZW length mismatch: got {output.Length} expected {expected}");
            return output;
        }

        public static byte[] Decompress(byte[] data, int start = 0)
        {
            int codeSize = 9;
            var dict = new List<byte[]>(4096);
            ResetDictionary(dict);
            var result = new List<byte>(data.Length * 3);
            uint bits = 0;
            int nbits = 0;
            int pos = start;
            int prev = -1;
            while (true)
            {
                while (nbits < codeSize && pos < data.Length)
                {
                    bits |= (uint)data[pos] << nbits;
                    nbits += 8;
                    pos++;
                }
                if (nbits < codeSize) break;
                int code = (int)(bits & ((1u << codeSize) - 1));
                bits >>= codeSize;
                nbits -= codeSize;
                if (code == Clear)
                {
                    ResetDictionary(dict);
                    codeSize = 9;
                    prev = -1;
                    continue;
                }
                if (code == End) break;

                byte[] entry;
                if (code < dict.Count)
                    entry = dict[code];
                else if (code == dict.Count && prev >= 0)
                    entry = Append(dict[prev], dict[prev][0]);
                else
                    break;

                result.AddRange(entry);
                if (prev >= 0)
                {
                    dict.Add(Append(dict[prev], entry[0]));
                    if (dict.Count == (1 << codeSize) && codeSize < 12) codeSize++;
                }
                prev = code;
            }
            return result.ToArray();
        }

        static void ResetDictionary(List<byte[]> dict)
        {
            dict.Clear();
            for (int i = 0; i < 256; i++) dict.Add(new[] { (byte)i });
            dict.Add(Array.Empty<byte>());
            dict.Add(Array.Empty<byte>());
        }

        static byte[] Append(byte[] a, byte b)
        {
            var r = new byte[a.Length + 1];
            Buffer.BlockCopy(a, 0, r, 0, a.Length);
            r[a.Length] = b;
            return r;
        }
    }

    public class U5FormatException : Exception
    {
        public U5FormatException(string message) : base(message) { }
    }
}
