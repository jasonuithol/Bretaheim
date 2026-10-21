using System.Collections.Generic;

namespace U5InValheim.Data
{
    /// <summary>
    /// Food (MASTER_PLAN §5.9, "Restrictions and Etc.txt"): rations are barley, and each character
    /// eats one from their inventory once a day; with none they lose 30 % of their health. (The
    /// original eats one ration a party member at 6, 12 and 18 o'clock and, with none, hurts
    /// everyone 1..8 each hour: rest.rs pass_turn.) The day last eaten is kept with the character.
    /// </summary>
    public static class Rations
    {
        public const string FedDayKey = "u5_fed_day";
        /// <summary>The Valheim item that is a ration (items.json "Food", used as it is).</summary>
        public const string Item = "Barley";
        /// <summary>The share of their greatest health a character without a ration loses.</summary>
        public const float Starving = 0.3f;

        /// <summary>
        /// Whether a meal is due on <paramref name="today"/> (Valheim's day number), marking it
        /// eaten: one meal when a new day has come since the last (days away are not counted
        /// against them); none for a character new to the world (they arrive fed) or when the
        /// world's days run behind the one remembered (another world).
        /// </summary>
        public static bool Due(IDictionary<string, string> data, int today)
        {
            bool known = data.TryGetValue(FedDayKey, out var v) && int.TryParse(v, out _);
            int last = known ? int.Parse(v) : today;
            if (known && last == today) return false;
            data[FedDayKey] = today.ToString();
            return known && today > last;
        }

        /// <summary>The health a starving character is left with (it can kill, as starving did).</summary>
        public static float AfterStarving(float health, float maxHealth) => health - Starving * maxHealth;
    }
}
