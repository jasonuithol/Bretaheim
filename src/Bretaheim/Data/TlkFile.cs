using System;
using System.Collections.Generic;
using System.Text;

namespace Bretaheim.Data
{
    /// <summary>Opcodes of the .TLK byte code (TALK.OVL interpret_byte / interpret_opcode_arg).</summary>
    public static class TlkOp
    {
        public const byte AvatarName = 0x81;
        public const byte End = 0x82;
        public const byte Pause = 0x83;
        public const byte JoinParty = 0x84;
        /// <summary>Followed by 3 digit bytes: pay that much gold.</summary>
        public const byte PayGold = 0x85;
        /// <summary>Followed by 1 byte: give item (b &amp; 0x7f).</summary>
        public const byte GiveItem = 0x86;
        /// <summary>"Or": run the string after the next one (keyword alternatives share an answer).</summary>
        public const byte Or = 0x87;
        public const byte AskName = 0x88;
        public const byte KarmaPlus = 0x89;
        public const byte KarmaMinus = 0x8a;
        public const byte CallGuards = 0x8b;
        /// <summary>Followed by 1 byte: if the NPC knows the Avatar, goto that label (0xff = "Your interest?").</summary>
        public const byte IfMetGoto = 0x8c;
        public const byte NewLine = 0x8d;
        public const byte RuneToggle = 0x8e;
        public const byte KeyWait = 0x8f;
        /// <summary>Ends the keyword list / starts a label block.</summary>
        public const byte LabelMark = 0x90;
        public const byte FirstLabel = 0x91;
        /// <summary>Label byte that ends the entry (0x90 0x9f).</summary>
        public const byte EndLabel = 0x9f;
        /// <summary>Followed by 2 bytes: karma ≥ K → goto label L.</summary>
        public const byte KarmaGoto = 0xfe;
        public const byte Interest = 0xff;

        public static int ArgCount(byte op)
        {
            switch (op)
            {
                case PayGold: return 3;
                case GiveItem: return 1;
                case IfMetGoto: return 1;
                case KarmaGoto: return 2;
                default: return 0;
            }
        }
    }

    public class TlkKeywordEntry
    {
        /// <summary>Keywords (as stored: 1..4 upper/lower-case letters; the game matches them case-insensitively at a word start).</summary>
        public List<string> Keys = new List<string>();
        public byte[] Answer;
    }

    public class TlkLabel
    {
        /// <summary>Label number 1..14 (byte 0x91..0x9e).</summary>
        public int Number;
        public byte[] Question;
        public byte[] DefaultAnswer;
        public List<TlkKeywordEntry> Keywords = new List<TlkKeywordEntry>();
    }

    /// <summary>One NPC's conversation: fixed sections, keywords, labels (see analysis/ovl_TALK.md).</summary>
    public class TlkScript
    {
        public int Npc;
        public byte[] Raw;
        public byte[] Name, Description, Greeting, Job, Bye;
        public List<TlkKeywordEntry> Keywords = new List<TlkKeywordEntry>();
        public List<TlkLabel> Labels = new List<TlkLabel>();

        public static TlkScript Parse(int npc, byte[] raw)
        {
            var s = new TlkScript { Npc = npc, Raw = raw };
            int p = 0;
            byte[] Next()
            {
                int start = p;
                while (p < raw.Length && raw[p] != 0) p++;
                var r = new byte[p - start];
                Buffer.BlockCopy(raw, start, r, 0, r.Length);
                if (p < raw.Length) p++; // NUL
                return r;
            }
            s.Name = Next();
            s.Description = Next();
            s.Greeting = Next();
            s.Job = Next();
            s.Bye = Next();

            // keyword/answer pairs until the 0x90 that starts the label blocks. Like seek_keyword
            // (which gives up at the first 0x90 met while skipping sections) the list also ends
            // when an answer would start with 0x90: Mario in Yew has a stray NUL inside an answer
            // that shifts every later section by one, in the original too.
            while (p < raw.Length && raw[p] != TlkOp.LabelMark)
            {
                var e = ReadKeywordEntry(raw, ref p, Next);
                if (e == null) break;
                s.Keywords.Add(e);
            }

            // label blocks: 0x90 <label> question NUL default NUL {kw NUL answer NUL}*
            while (p + 1 < raw.Length && raw[p] == TlkOp.LabelMark && raw[p + 1] != TlkOp.EndLabel)
            {
                byte label = raw[p + 1];
                if (label < TlkOp.FirstLabel || label >= TlkOp.EndLabel)
                    throw new U5FormatException($"TLK npc {npc}: bad label byte 0x{label:x2} at {p + 1}");
                p += 2;
                var l = new TlkLabel { Number = label - 0x90, Question = Next() };
                if (p < raw.Length && raw[p] != TlkOp.LabelMark)
                {
                    l.DefaultAnswer = Next();
                    while (p < raw.Length && raw[p] != TlkOp.LabelMark)
                    {
                        var e = ReadKeywordEntry(raw, ref p, Next);
                        if (e == null) break;
                        l.Keywords.Add(e);
                    }
                }
                s.Labels.Add(l);
            }
            return s;
        }

        static TlkKeywordEntry ReadKeywordEntry(byte[] raw, ref int p, Func<byte[]> next)
        {
            var e = new TlkKeywordEntry();
            while (true)
            {
                e.Keys.Add(TlkText.Plain(next()));
                if (p >= raw.Length || raw[p] == TlkOp.LabelMark) return null; // keyword without an answer
                var answer = next();
                // An answer of just 0x87 means "or": the next keyword shares the answer after it.
                if (answer.Length == 1 && answer[0] == TlkOp.Or && p < raw.Length && raw[p] != TlkOp.LabelMark) continue;
                e.Answer = answer;
                return e;
            }
        }
    }

    /// <summary>A .TLK file: u16 count, count × (u16 npc, u16 offset), then the scripts.</summary>
    public class TlkFile
    {
        readonly Dictionary<int, TlkScript> scripts = new Dictionary<int, TlkScript>();

        public TlkFile(byte[] data)
        {
            int count = data[0] | data[1] << 8;
            var npcs = new int[count];
            var offsets = new int[count + 1];
            for (int i = 0; i < count; i++)
            {
                npcs[i] = data[2 + 4 * i] | data[3 + 4 * i] << 8;
                offsets[i] = data[4 + 4 * i] | data[5 + 4 * i] << 8;
            }
            offsets[count] = data.Length;
            for (int i = 0; i < count; i++)
            {
                int len = offsets[i + 1] - offsets[i];
                if (len <= 0 || offsets[i] + len > data.Length)
                    throw new U5FormatException($"TLK entry {npcs[i]} has a bad offset");
                var raw = new byte[len];
                Buffer.BlockCopy(data, offsets[i], raw, 0, len);
                scripts[npcs[i]] = TlkScript.Parse(npcs[i], raw);
            }
        }

        public IEnumerable<int> Npcs => scripts.Keys;
        public int Count => scripts.Count;
        public TlkScript this[int dialogNumber] => scripts.TryGetValue(dialogNumber, out var s) ? s : null;
    }

    /// <summary>
    /// Turns .TLK bytes into text the way TALK.OVL emits them: bytes 0xa0..0xfe are characters
    /// (| 0x80), bytes &lt; 0x81 are compressed words (DATA.OVL g_tlk_word_ptrs) emitted with a
    /// leading space and followed by a space before the next character, 0x8d is a new line.
    /// Opcodes are rendered as readable markers.
    /// </summary>
    public static class TlkText
    {
        /// <summary>Keyword sections are plain characters.</summary>
        public static string Plain(byte[] b)
        {
            var sb = new StringBuilder(b.Length);
            foreach (byte c in b) sb.Append((char)(c & 0x7f));
            return sb.ToString();
        }

        /// <param name="words">The compressed word table (code → word, null if unused).</param>
        /// <param name="avatarName">Substituted for 0x81; null keeps a marker.</param>
        public static string Render(byte[] b, Func<int, string> words, string avatarName = null, bool markers = true)
        {
            var sb = new StringBuilder();
            bool needSpace = false;
            bool runes = false; // toggled by 0x8e
            for (int i = 0; i < b.Length; i++)
            {
                byte c = b[i];
                int argc = TlkOp.ArgCount(c);
                if (argc > 0)
                {
                    var args = new byte[Math.Min(argc, b.Length - i - 1)];
                    Buffer.BlockCopy(b, i + 1, args, 0, args.Length);
                    i += args.Length;
                    if (markers) sb.Append(OpMarker(c, args));
                    continue;
                }
                if (c < 0x81)
                {
                    string w = words(c);
                    if (string.IsNullOrEmpty(w)) { sb.Append((char)c); continue; }
                    sb.Append(' ').Append(w);
                    needSpace = true;
                    continue;
                }
                if (c >= 0xa0 && c < 0xfe)
                {
                    if (needSpace) sb.Append(' ');
                    needSpace = false;
                    // In rune mode (0x8e) the same letters are drawn in the runic font; the
                    // <runes> markers carry that, the letters stay readable.
                    sb.Append((char)(c & 0x7f));
                    continue;
                }
                switch (c)
                {
                    case TlkOp.AvatarName:
                        if (needSpace) sb.Append(' ');
                        needSpace = false;
                        sb.Append(avatarName ?? (markers ? "<avatar>" : "Avatar"));
                        break;
                    case TlkOp.NewLine: sb.Append('\n'); needSpace = false; break;
                    case TlkOp.RuneToggle: runes = !runes; if (markers) sb.Append(runes ? "<runes>" : "</runes>"); break;
                    default:
                        if (markers) sb.Append(OpMarker(c, Array.Empty<byte>()));
                        break;
                }
            }
            return sb.ToString();
        }

        static string OpMarker(byte op, byte[] args)
        {
            switch (op)
            {
                case TlkOp.End: return "<end>";
                case TlkOp.Pause: return "<pause>";
                case TlkOp.JoinParty: return "<join>";
                case TlkOp.PayGold:
                    var digits = new StringBuilder();
                    foreach (byte a in args) digits.Append((char)(a & 0x7f));
                    return $"<pay {digits}>";
                case TlkOp.GiveItem: return args.Length > 0 ? $"<give 0x{args[0] & 0x7f:x2}>" : "<give>";
                case TlkOp.Or: return "<or>";
                case TlkOp.AskName: return "<ask-name>";
                case TlkOp.KarmaPlus: return "<karma+1>";
                case TlkOp.KarmaMinus: return "<karma-1>";
                case TlkOp.CallGuards: return "<guards>";
                case TlkOp.IfMetGoto: return args.Length > 0 ? (args[0] == 0xff ? "<if-met interest>" : $"<if-met goto {args[0] - 0x90}>") : "<if-met>";
                case TlkOp.KeyWait: return "<key>";
                case TlkOp.KarmaGoto: return args.Length == 2 ? $"<if karma>={args[0]} goto {args[1] - 0x90}>" : "<karma-goto>";
                case TlkOp.Interest: return "<interest>";
                default:
                    if (op > TlkOp.LabelMark && op < TlkOp.EndLabel) return $"<goto {op - 0x90}>";
                    return $"<0x{op:x2}>";
            }
        }
    }
}
