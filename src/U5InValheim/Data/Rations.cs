using System;
using System.Collections.Generic;

namespace U5InValheim.Data
{
    /// <summary>
    /// Food (MASTER_PLAN §5.9, "Restrictions and Etc.txt"): rations are barley, carried by each
    /// character. As the original (rest.rs end_of_turn_party_update), when the hour changes a
    /// character with no ration is "Starving!" and hurt 1..8 hit points; otherwise one ration is
    /// eaten at 6, 12 and 18 o'clock. The hour last seen is kept with the character.
    /// </summary>
    public static class Rations
    {
        public const string HourKey = "u5_fed_hour";
        /// <summary>The Valheim item that is a ration (items.json "Food", used as it is).</summary>
        public const string Item = "Barley";
        public static readonly int[] MealHours = { 6, 12, 18 };

        /// <summary>
        /// Whether the hour has changed since the character last looked (<paramref name="hour"/>:
        /// hours since the world began), marking it seen. Hours passed at once (sleep, time away)
        /// count as one change, as the original checks once a turn; a character new to the world,
        /// or come from another world whose hours ran further, only starts counting.
        /// </summary>
        public static bool NewHour(IDictionary<string, string> data, long hour)
        {
            bool known = data.TryGetValue(HourKey, out var v) && long.TryParse(v, out _);
            long last = known ? long.Parse(v) : hour;
            if (known && last == hour) return false;
            data[HourKey] = hour.ToString();
            return known && hour > last;
        }

        /// <summary>A ration is eaten at this hour of the day (one a character).</summary>
        public static bool Mealtime(int hourOfDay) => Array.IndexOf(MealHours, hourOfDay) >= 0;

        /// <summary>The hit points starving takes in an hour (damage_whole_party: 1..8).</summary>
        public static int StarvingDamage(Random rnd) => rnd.Next(1, 9);
    }
}
