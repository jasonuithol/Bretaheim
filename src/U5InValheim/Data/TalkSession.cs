using System;
using System.Collections.Generic;
using System.Text;

namespace U5InValheim.Data
{
    /// <summary>What a conversation can read and change in the game (TALK.OVL's globals).</summary>
    public interface ITalkWorld
    {
        /// <summary>The name of the Avatar talking (party record 0).</summary>
        string AvatarName { get; }
        /// <summary>The names an answer to "What is thy name?" is matched against (the party).</summary>
        IEnumerable<string> PartyNames { get; }
        bool Met { get; set; }
        int Karma { get; set; }
        int Gold { get; }
        void TakeGold(int amount);
        /// <summary>TALK.OVL give_item ids: &lt; 0x40 inventory, 0x41 food, 0x42 gold, 0x43 keys, 0x44 gems, 0x45 torches, 0x46 grapple, 0x47 carpet, 0x48 sextant, 0x49 spyglass, 0x4a badge, 0x4b skull key.</summary>
        void Give(int item);
        /// <summary>The NPC asks to join the party (0x84); true when they did.</summary>
        bool Join();
        void Alarm();
        /// <summary>A fair coin (greet_npc's 50 % "I am called").</summary>
        bool Coin();
    }

    /// <summary>
    /// One conversation with a townsperson: TALK.OVL run_conversation (Rust port
    /// rust/src/ovl/talk.rs) as a state machine. Where the original reads a line it pauses
    /// (<see cref="Waiting"/>, <see cref="Prompt"/>) until <see cref="Respond"/>; its key waits and
    /// pauses do not stop. Text goes to <see cref="TakeOutput"/>. Strings come from DATA.OVL.
    /// </summary>
    public class TalkSession
    {
        // DATA.OVL strings (DGROUP)
        const int BuiltinKeywords = 0x4aa8, BuiltinCount = 0x22;
        const int SMyNameIs = 0x93c2, SLanguage = 0x93d0, SInterest = 0x9408, SNoResponse = 0x941a, SCannotHelp = 0x9420,
            SRespond = 0x9440, SWhatSay = 0x9450, SWhatName = 0x9468, SRespond2 = 0x947c, SIfYouSaySo = 0x948c,
            SPleasure = 0x94a0, SIfYouSaySo2 = 0x94b0, SYouSee = 0x94c4, SIAmCalled = 0x94ce, SNotEnoughGold = 0x9328,
            SNotEnoughGold2 = 0x9344, SNoRoom = 0x9348, SNoRoom2 = 0x9372;
        const int InputMax = 15;

        readonly byte[] raw;
        readonly DataOvl ovl;
        readonly ITalkWorld world;
        readonly IEnumerator<bool> program;
        readonly StringBuilder output = new StringBuilder();

        int pos, pendingOp, argc, label, keywordIdx;
        readonly byte[] args = new byte[3];
        bool asking, needSpace, runes;
        byte lastChar;
        string input = "";

        /// <summary>The conversation is over (bye, or the script ended it).</summary>
        public bool Over { get; private set; }
        /// <summary>Waiting for a line from the player; <see cref="Prompt"/> says for what.</summary>
        public bool Waiting { get; private set; }
        public string Prompt { get; private set; } = "";
        /// <summary>The words that end a conversation politely (for a Bye button).</summary>
        public const string Bye = "bye";

        public TalkSession(TlkScript script, DataOvl ovl, ITalkWorld world)
        {
            raw = script.Raw;
            this.ovl = ovl;
            this.world = world;
            program = Run().GetEnumerator();
            Step();
        }

        /// <summary>Everything said since the last call.</summary>
        public string TakeOutput()
        {
            var s = output.ToString();
            output.Clear();
            return s;
        }

        /// <summary>The player's line (cut to the original's 15 characters).</summary>
        public void Respond(string line)
        {
            if (Over || !Waiting) return;
            line = (line ?? "").Trim();
            input = line.Length > InputMax ? line.Substring(0, InputMax) : line;
            output.Append(input).Append('\n');
            Step();
        }

        void Step()
        {
            Waiting = false;
            if (program.MoveNext()) Waiting = true;
            else Over = true;
        }

        // ── the original's routines; an iterator yields once per line read, R holds its result ──

        sealed class R { public int V; }

        IEnumerable<bool> Run()
        {
            var r = new R();
            foreach (var w in GreetNpc(r)) yield return w;
            if (r.V == 0)
            {
                foreach (var w in InterestLoop(r)) yield return w;
                if (r.V == 0) foreach (var w in SayBye(r)) yield return w;
            }
        }

        /// <summary>Reads the player's response (a pause for <see cref="Respond"/>).</summary>
        IEnumerable<bool> ReadResponse(string prompt)
        {
            Prompt = prompt;
            yield return true;
        }

        void Print(int dg) => output.Append(ovl.CStr(dg));
        void Quote() => InterpretText(0xa2);
        void NewLine() => InterpretText(0x8d);

        IEnumerable<bool> GreetNpc(R r)
        {
            Print(SYouSee);
            foreach (var w in RunSection(0 + 1, r)) yield return w;
            if (r.V != 0) yield break;
            NewLine();
            NewLine();
            int section;
            if (!world.Met)
            {
                if (!world.Coin()) { r.V = 0; yield break; }
                Print(SIAmCalled);
                section = 0;
            }
            else
            {
                Quote();
                section = 2;
            }
            foreach (var w in RunSection(section, r)) yield return w;
            if (r.V != 0) yield break;
            Quote();
            NewLine();
            NewLine();
            r.V = 0;
        }

        IEnumerable<bool> InterestLoop(R r)
        {
            while (true)
            {
                asking = false;
                Print(SInterest);
                foreach (var w in ReadResponse(ovl.CStr(SInterest))) yield return w;
                if (input.Length == 0)
                {
                    Print(SNoResponse);
                    foreach (var w in SayBye(r)) yield return w;
                    yield break;
                }
                NewLine();
                NewLine();
                int res = -1;
                for (int idx = 0; idx < BuiltinCount; idx++)
                {
                    if (!MatchedAtWord(input, ovl.PtrStr(BuiltinKeywords, idx))) continue;
                    var b = new R();
                    foreach (var w in HandleBuiltin(idx, b)) yield return w;
                    res = b.V;
                    if (res == 0) break;
                    if (res == 1) { r.V = 1; yield break; }
                }
                if (res != 0)
                {
                    if (!FindNpcKeyword())
                        Print(SCannotHelp);
                    else
                    {
                        Quote();
                        foreach (var w in RunSection(keywordIdx * 2 + 6, r)) yield return w;
                        if (r.V != 0) { r.V = 1; yield break; }
                    }
                    Quote();
                    NewLine();
                    NewLine();
                }
            }
        }

        /// <summary>0 NAME, 1-2 JOB/WORK, 3-4 BYE/THANK, the rest insults; while answering a question the first five are not handled (2).</summary>
        IEnumerable<bool> HandleBuiltin(int idx, R r)
        {
            var s = new R();
            if (idx < 5 && asking) { r.V = 2; yield break; }
            if (idx == 0)
            {
                Print(SMyNameIs);
                foreach (var w in RunSection(0, s)) yield return w;
            }
            else if (idx < 3)
            {
                Quote();
                foreach (var w in RunSection(3, s)) yield return w;
            }
            else if (idx < 5)
            {
                foreach (var w in SayBye(r)) yield return w;
                yield break;
            }
            else
            {
                Print(SLanguage);
                Quote();
                NewLine();
                NewLine();
                r.V = 0;
                yield break;
            }
            if (s.V != 0) { r.V = 1; yield break; }
            Quote();
            NewLine();
            NewLine();
            r.V = 0;
        }

        IEnumerable<bool> SayBye(R r)
        {
            Quote();
            var s = new R();
            foreach (var w in RunSection(4, s)) yield return w;
            if (s.V == 0)
            {
                Quote();
                NewLine();
            }
            r.V = 1;
        }

        IEnumerable<bool> AskQuestionAtLabel(R r)
        {
            var s = new R();
            while (true)
            {
                Quote();
                foreach (var w in RunLabelText(s)) yield return w;
                if (s.V != 0) { r.V = 1; yield break; }
                Quote();
                do
                {
                    NewLine();
                    NewLine();
                    asking = true;
                    Print(SRespond);
                    foreach (var w in ReadResponse(ovl.CStr(SRespond))) yield return w;
                    if (input.Length == 0) Print(SWhatSay);
                } while (input.Length == 0);
                NewLine();
                NewLine();
                int res = -1;
                for (int idx = 0; idx < BuiltinCount; idx++)
                {
                    if (!MatchedAtWord(input, ovl.PtrStr(BuiltinKeywords, idx))) continue;
                    var b = new R();
                    foreach (var w in HandleBuiltin(idx, b)) yield return w;
                    res = b.V;
                    if (res == 0) break;
                    if (res == 1) { r.V = 1; yield break; }
                }
                if (res != 0) break; // not a built-in: the answer to the question
            }
            if (!FindLabelKeyword())
            {
                Quote();
                SeekLabel();
                SkipUntil(0x9f, 0);
            }
            else
            {
                Quote();
                SkipUntil(0x90, 0);
            }
            foreach (var w in RunScript(s)) yield return w;
            if (s.V == 0)
            {
                Quote();
                NewLine();
                NewLine();
                foreach (var w in InterestLoop(r)) yield return w;
                yield break;
            }
            r.V = 1;
        }

        IEnumerable<bool> AskAvatarName()
        {
            Quote();
            Print(SWhatName);
            Print(SRespond2);
            foreach (var w in ReadResponse(ovl.CStr(SWhatName).Trim('"'))) yield return w;
            argc = 0;
            pendingOp = 0;
            if (input.Length > 0)
                foreach (var name in world.PartyNames)
                {
                    var key = name.Length > 4 ? name.Substring(0, 4) : name;
                    if (key.Length > 0 && MatchedAtWord(input, key))
                    {
                        world.Met = true;
                        Print(SPleasure);
                        yield break;
                    }
                }
            Print(input.Length == 0 ? SIfYouSaySo : SIfYouSaySo2);
        }

        // ── script walking ──────────────────────────────────────────────────────────────────

        int Byte(int p) => p < raw.Length ? raw[p] : 0;

        /// <summary>Skips bytes until one equals end (1) or stop (0); the byte met is consumed.</summary>
        int SkipUntil(int stop, int end)
        {
            while (pos < raw.Length)
            {
                int c = raw[pos++];
                if (c == end) return 1;
                if (c == stop) return 0;
            }
            return 0; // the end of the entry
        }

        void SeekSection(int n)
        {
            pos = 0;
            while (n-- > 0) SkipUntil(0x90, 0);
        }

        void SeekLabel()
        {
            pos = 0;
            while (pos < raw.Length && Byte(pos) != label) SkipUntil(0x9f, 0x90);
        }

        IEnumerable<bool> RunSection(int n, R r)
        {
            SeekSection(n);
            foreach (var w in RunScript(r)) yield return w;
        }

        IEnumerable<bool> RunLabelText(R r)
        {
            SeekLabel();
            pos++;
            foreach (var w in RunScript(r)) yield return w;
        }

        /// <summary>Interprets bytes until a NUL (0) or until one ends the conversation (1).</summary>
        IEnumerable<bool> RunScript(R r)
        {
            var b = new R();
            while (pos < raw.Length && raw[pos] != 0)
            {
                byte c = raw[pos++];
                foreach (var w in InterpretByte(c, b)) yield return w;
                if (b.V != 0) { r.V = 1; yield break; }
            }
            r.V = 0;
        }

        bool SeekKeyword(int k)
        {
            pos = 0;
            for (int n = k * 2 + 5; n > 0; n--)
                if (SkipUntil(0x90, 0) == 0) return false;
            return true;
        }

        bool FindNpcKeyword()
        {
            for (keywordIdx = 0; ; keywordIdx++)
            {
                if (!SeekKeyword(keywordIdx)) return false;
                if (MatchedAtWord(input, StringAt(pos))) return true;
            }
        }

        bool FindLabelKeyword()
        {
            SeekLabel();
            SkipUntil(0x9f, 0);
            SkipUntil(0x9f, 0);
            while (true)
            {
                if (MatchedAtWord(input, StringAt(pos))) return true;
                if (SkipUntil(0x90, 0) == 0) return false;
                if (SkipUntil(0x90, 0) == 0) return false;
            }
        }

        string StringAt(int p)
        {
            var sb = new StringBuilder();
            while (p < raw.Length && raw[p] != 0 && raw[p] != 0x90) sb.Append((char)(raw[p++] & 0x7f));
            return sb.ToString();
        }

        /// <summary>str_find_ci's first match of the needle in the line, at its start or after a space.</summary>
        static bool MatchedAtWord(string line, string needle)
        {
            if (needle.Length == 0 || needle.Length > line.Length) return false;
            int m = line.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            return m == 0 || (m > 0 && line[m - 1] == ' ');
        }

        // ── the byte interpreter ────────────────────────────────────────────────────────────

        IEnumerable<bool> InterpretByte(byte b, R r)
        {
            r.V = 0;
            if (pendingOp != 0)
            {
                foreach (var w in InterpretOpcodeArg(b, r)) yield return w;
                yield break;
            }
            argc = 0;
            switch (b)
            {
                case 0x81: output.Append(needSpace ? " " : "").Append(world.AvatarName); needSpace = false; lastChar = 0; yield break;
                case 0x82: r.V = 1; yield break;
                case 0x83: case 0x8f: yield break; // pause / key wait
                case 0x84:
                    if (world.Join()) { r.V = 1; yield break; }
                    Print(SNoRoom);
                    Print(SNoRoom2);
                    yield break;
                case 0x85: case 0x86: case 0x8c: case 0xfe: pendingOp = b; yield break;
                case 0x87:
                {
                    int saved = pos;
                    // skip the byte after the opcode (and NULs), then the next string; run the one after
                    int p = pos - 1;
                    do p++; while (Byte(p) == 0 && p < raw.Length);
                    while (p < raw.Length && raw[p++] != 0) { }
                    pos = p;
                    var s = new R();
                    foreach (var w in RunScript(s)) yield return w;
                    if (s.V != 0) { r.V = 1; yield break; }
                    pos = saved;
                    yield break;
                }
                case 0x88:
                    foreach (var w in AskAvatarName()) yield return w;
                    yield break;
                case 0x89: world.Karma = Math.Min(99, world.Karma + 1); yield break;
                case 0x8a: world.Karma = Math.Max(0, world.Karma - 1); yield break;
                case 0x8b: world.Alarm(); yield break;
                case 0x8e: runes = !runes; yield break;
                case 0xff:
                    foreach (var w in InterestLoop(r)) yield return w;
                    yield break;
            }
            if (b > 0x90 && b < 0xa0)
            {
                label = b;
                foreach (var w in AskQuestionAtLabel(r)) yield return w;
                yield break;
            }
            InterpretText(b);
        }

        IEnumerable<bool> InterpretOpcodeArg(byte b, R r)
        {
            r.V = 0;
            int op = pendingOp;
            if (op == 0x85)
            {
                args[argc++] = b;
                if (argc != 3) yield break;
                pendingOp = 0;
                argc = 0;
                int price = (args[0] & 0x7f) * 100 + (args[1] & 0x7f) * 10 + (args[2] & 0x7f) - 0x14d0;
                if (world.Gold < price)
                {
                    output.Append('"');
                    Print(SNotEnoughGold);
                    output.Append('"');
                    Print(SNotEnoughGold2);
                    foreach (var w in InterestLoop(r)) yield return w;
                    yield break;
                }
                world.TakeGold(price);
                yield break;
            }
            if (op == 0x86)
            {
                world.Give(b & 0x7f);
                pendingOp = 0;
                argc = 0;
                yield break;
            }
            if (op == 0x8c)
            {
                pendingOp = 0;
                argc = 0;
                if (!world.Met) yield break;
                if (b == 0xff)
                {
                    foreach (var w in InterestLoop(r)) yield return w;
                    yield break;
                }
                label = b;
                foreach (var w in AskQuestionAtLabel(r)) yield return w;
                yield break;
            }
            if (op == 0xfe)
            {
                args[argc++] = b;
                if (argc != 2) yield break;
                pendingOp = 0;
                argc = 0;
                if (world.Karma < args[0]) yield break;
                label = args[1];
                foreach (var w in AskQuestionAtLabel(r)) yield return w;
                yield break;
            }
            pendingOp = 0;
            argc = 0;
        }

        /// <summary>Text bytes: compressed words (&lt; 0x81, with a space before) and characters (| 0x80; 0x8d new line).</summary>
        void InterpretText(byte b)
        {
            if (b == 0xa2 && lastChar == 0xa2) return; // never two quotes in a row
            if (b < 0x81)
            {
                string word = ovl.TlkWord(b);
                output.Append(' ');
                if (string.IsNullOrEmpty(word)) output.Append((char)b);
                else
                {
                    output.Append(word);
                    needSpace = true;
                }
                return;
            }
            byte c = (byte)(b | 0x80);
            lastChar = c;
            if (c == 0x8d)
            {
                output.Append('\n');
                needSpace = false;
                return;
            }
            if (needSpace) output.Append(' ');
            output.Append((char)(c & 0x7f));
            needSpace = false;
        }
    }
}
