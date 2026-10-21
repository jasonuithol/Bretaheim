using System;
using System.Collections.Generic;

namespace Bretaheim.Data
{
    /// <summary>
    /// The Avatar's karma (save 0x2E2, 0..99; INIT.GAM starts it at 50), kept with the Valheim
    /// character as <see cref="Key"/> (MASTER_PLAN §5.9: shown as a skill on the Tab screen). The
    /// original changes it in a handful of places; each is a constant here with where it comes
    /// from, so the game's hooks only say which deed was done.
    /// </summary>
    public static class Karma
    {
        /// <summary>The character custom-data key (also written by TALK.OVL's 0x89/0x8a through the conversation).</summary>
        public const string Key = "u5_karma";
        public const int Start = 50, Max = 99;

        // ── the original's deeds (Rust port: rest::add_u8_capped / sub_u8_floor on g::KARMA) ──
        /// <summary>TOWN cmd_attack: attacking a townsperson (an object below 0x80) costs 5 and raises the alarm.</summary>
        public const int AttackTownsperson = -5;
        /// <summary>TOWN cmd_attack: "Murdered!" (striking one lying in their bed): 5 more.</summary>
        public const int Murder = -5;
        /// <summary>SJOG open_chest: opening a chest in a settlement (location 1..0x20) costs 2.</summary>
        public const int OpenTownChest = -2;
        /// <summary>SJOG get_item: "Crops picked!" (and food from a plate) costs 1.</summary>
        public const int PickCrops = -1;
        /// <summary>TALK pay_gold_price: paying a beggar (at most once every 100 turns) gives 1 …</summary>
        public const int Charity = 1;
        /// <summary>… and 2 more when it took thy last coin.</summary>
        public const int CharityLastCoin = 2;
        /// <summary>SJOG use_key: freeing a prisoner ("I thank thee!") gives 2.</summary>
        public const int FreePrisoner = 2;
        /// <summary>CMDS cmd_fire: a cannonball striking someone costs 5.</summary>
        public const int CannonHit = -5;
        /// <summary>SHOPPES2 order_drink: "No!" to "haven't ye had enough to drink?" costs 1.</summary>
        public const int Drunk = -1;
        /// <summary>BLCKTHRN interrogation: giving up a mantra costs 5.</summary>
        public const int GiveUpMantra = -5;

        /// <summary>The settlements whose chests cost karma to open (open_chest: location 1..0x20: towns, dwellings, castles and keeps).</summary>
        public static bool IsSettlement(int location) => location >= 1 && location <= 0x20;

        /// <summary>How long between two beggars' karma: the original's 100 turns, a turn taken as an Ultima minute (Valheim seconds of game time).</summary>
        public static double CharityInterval(double secondsPerUltimaMinute) => 100 * secondsPerUltimaMinute;

        /// <summary>
        /// The karma for paying a beggar (pay_gold_price): +1 when the last such payment was at
        /// least <paramref name="interval"/> ago (never: <paramref name="lastAt"/> negative), +2 more
        /// when it left no gold.
        /// </summary>
        public static int ForCharity(double now, double lastAt, double interval, int goldLeft)
        {
            if (lastAt >= 0 && now - lastAt < interval) return 0;
            return Charity + (goldLeft == 0 ? CharityLastCoin : 0);
        }

        /// <summary>A beggar (pay_gold_price: the NPC's tile group 0x6c).</summary>
        public static bool IsBeggar(SettlementNpc npc) => npc != null && (npc.Type & 0xfc) == 0x6c;

        public static int Clamp(int k) => Math.Max(0, Math.Min(Max, k));

        /// <summary>The karma kept in a character's custom data (<see cref="Start"/> when none is).</summary>
        public static int Get(IDictionary<string, string> data)
            => data != null && data.TryGetValue(Key, out var v) && int.TryParse(v, out int k) ? Clamp(k) : Start;

        public static void Set(IDictionary<string, string> data, int karma)
        {
            if (data != null) data[Key] = Clamp(karma).ToString();
        }

        /// <summary>Adds (or takes) karma, kept within 0..99; returns the new karma.</summary>
        public static int Add(IDictionary<string, string> data, int delta)
        {
            int k = Clamp(Get(data) + delta);
            Set(data, k);
            return k;
        }
    }
}
