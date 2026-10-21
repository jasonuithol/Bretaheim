using System;
using System.IO;

namespace U5InValheim.Data
{
    /// <summary>
    /// The gypsy's casting (FONT create_new_character, ask_gypsy_question; Rust port ovl/font.rs):
    /// seven questions, each between two of the eight virtue cards (Honesty, Compassion, Valor,
    /// Justice, Sacrifice, Honor, Spirituality, Humility) not yet shown this round nor rejected;
    /// rounds of 4, 2 and 1 questions. Answer A is always the lower card. The chosen card's
    /// intelligence, dexterity and strength bonuses (DATA.OVL) are added to INIT.GAM's Avatar; the
    /// rejected card is out. At the end intelligence and mana are the intelligence, strength at
    /// least 20.
    /// </summary>
    public class VirtueCasting
    {
        public const int Cards = 8, Questions = 7;
        // DATA.OVL (DGROUP)
        public const int IntBonus = 0x5164, DexBonus = 0x516c, StrBonus = 0x5174, QuestionOffsets = 0x517c, ClosingText = 0x322;
        public static readonly string[] Names = { "Honesty", "Compassion", "Valor", "Justice", "Sacrifice", "Honor", "Spirituality", "Humility" };
        static readonly int[] RoundEnds = { 4, 6, 7 };

        readonly DataOvl ovl;
        readonly Random random;
        readonly bool[] shown = new bool[Cards], rejected = new bool[Cards];

        public int Intelligence, Dexterity, Strength;
        /// <summary>Questions answered so far (0..7).</summary>
        public int Asked { get; private set; }
        public bool Done => Asked >= Questions;
        /// <summary>The cards of the question being asked (picked in this order), or −1.</summary>
        public int First = -1, Second = -1;

        public VirtueCasting(DataOvl ovl, CharacterRecord start, Random random)
        {
            this.ovl = ovl;
            this.random = random;
            Intelligence = start?.Int ?? 15;
            Dexterity = start?.Dex ?? 15;
            Strength = start?.Str ?? 15;
        }

        /// <summary>The lower card (answer A) and the higher (answer B) of the question.</summary>
        public int Left => Math.Min(First, Second);
        public int Right => Math.Max(First, Second);

        /// <summary>QUESTION.DAT offset of the question between the two cards.</summary>
        public int QuestionOffset => ovl.U16(QuestionOffsets + Second * 2 + First * 16);

        /// <summary>pick_unused_card: a random card neither shown this round nor rejected.</summary>
        int Pick()
        {
            int i;
            do { i = random.Next(0, Cards); } while (shown[i] || rejected[i]);
            shown[i] = true;
            return i;
        }

        /// <summary>The next question: two cards picked (a new round clears the shown cards).</summary>
        public void Next()
        {
            if (Done) throw new InvalidOperationException("the casting is over");
            if (Asked == 4 || Asked == 6) Array.Clear(shown, 0, Cards);
            First = Pick();
            Second = Pick();
        }

        /// <summary>The answer: 'A' takes the lower card, 'B' the higher; the other is rejected. Returns the chosen card.</summary>
        public int Answer(char letter)
        {
            letter = char.ToUpperInvariant(letter);
            if (First < 0 || (letter != 'A' && letter != 'B')) throw new ArgumentException("A or B");
            int chosen = letter == 'A' ? Left : Right, other = chosen == First ? Second : First;
            Intelligence += ovl.U8(IntBonus + chosen);
            Dexterity += ovl.U8(DexBonus + chosen);
            Strength += ovl.U8(StrBonus + chosen);
            rejected[other] = true;
            Asked++;
            First = Second = -1;
            return chosen;
        }

        /// <summary>The Avatar the casting made: intelligence and mana the intelligence, dexterity, strength at least 20.</summary>
        public void Apply(Avatar a)
        {
            a.Intelligence = Math.Min(Avatar.MaxStat, Intelligence);
            a.Mana = a.Intelligence;
            a.Dexterity = Math.Min(Avatar.MaxStat, Dexterity);
            a.Strength = Math.Min(Avatar.MaxStat, Math.Max(20, Strength));
        }

        // ── the screens (CREATE.16, QUESTION.DAT) ──────────────────────────────────────────

        /// <summary>The files the gypsy needs.</summary>
        public static bool Available(string dir)
            => U5Data.FindFile(dir, "QUESTION.DAT") != null && U5Data.FindFile(dir, "CREATE.16") != null && U5Data.FindFile(dir, "PROPORT.PCS") != null;

        /// <summary>The gypsy's pictures and words, read once.</summary>
        public class Art
        {
            public readonly ProportionalFont Font;
            public readonly PictureFile Create;
            public readonly byte[] Questions;
            readonly DataOvl ovl;

            public Art(string dir, DataOvl ovl)
            {
                byte[] Read(string name) => File.ReadAllBytes(U5Data.FindFile(dir, name));
                this.ovl = ovl;
                Font = new ProportionalFont(Read("PROPORT.PCS"), ovl);
                Create = new PictureFile(Read("CREATE.16"));
                Questions = Read("QUESTION.DAT");
            }

            /// <summary>The wagon (CREATE.16 picture 0) and the meeting with the gypsy (QUESTION.DAT 0).</summary>
            public Screen Opening(out string text)
            {
                var s = new Screen();
                s.Draw(Create[0], 0, 0x60);
                var t = Screen.TextAt(Questions, 0);
                s.Text(t, Font, new TextLayout { Left0 = 0, Right0 = 0x140, Left1 = 0xaf, Right1 = 0x140, Band1From = 0x59, Band1To = 200, X = 0, Y = 9, Space = Font.DefaultSpace });
                text = Screen.Plain(t);
                return s;
            }

            /// <summary>A question (ask_gypsy_question): the two card frames, the two cards, the question.</summary>
            public Screen Question(VirtueCasting c, out string text)
            {
                var s = new Screen();
                s.Draw(Create[1], 0x10, 0);
                s.Draw(Create[1], 200, 0);
                int l = c.Left, r = c.Right;
                s.Draw(Create[l + 2], ovl.U8(Dg.CreateImageX + l), ovl.U8(Dg.CreateImageY + l));
                s.Draw(Create[r + 2], ovl.U8(Dg.CreateImageX + r) + 0xb8, ovl.U8(Dg.CreateImageY + r));
                var t = Screen.TextAt(Questions, c.QuestionOffset);
                s.Text(t, Font, new TextLayout { Left0 = 0, Right0 = 0x140, Left1 = 0xaf, Right1 = 0x140, Band1From = 200, Band1To = 200, X = 0, Y = 0x98, Space = Font.DefaultSpace });
                text = Screen.Plain(t);
                return s;
            }

            /// <summary>The end of the casting (QUESTION.DAT 0x322, CREATE.16 picture 10).</summary>
            public Screen Closing(out string text)
            {
                var s = new Screen();
                s.Draw(Create[10], 0xa8, 100);
                var t = Screen.TextAt(Questions, ClosingText);
                s.Text(t, Font, new TextLayout { Left0 = 0, Right0 = 0x140, Left1 = 0, Right1 = 0xa6, Band1From = 0x5a, Band1To = 200, X = 0, Y = 0, Space = 4 });
                text = Screen.Plain(t);
                return s;
            }
        }
    }
}
