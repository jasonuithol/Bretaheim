using System;
using System.Collections;
using System.Linq;
using Bretaheim.WorldGen;
using Bretaheim.WorldGen.Core;
using UnityEngine;

namespace Bretaheim.DevTools
{
    /// <summary>
    /// U5_SELFTEST=clock on a dedicated server (run by TownsfolkSelfTest): the calendar, the moons
    /// and the moonstone sites; a moonstone moved and put back through the world's keys; a
    /// Shadowlord in Britain (its body, and that it leaves when the calendar has it elsewhere) and
    /// the townsfolk its air touches; and a Shadowlord called to the Flame of Truth and destroyed
    /// there with its shard (the doom taken back afterwards).
    /// </summary>
    public static class ClockSelfTest
    {
        static void Log(string s) => Plugin.Log.LogInfo("clock self-test " + s);

        public static IEnumerator Run(Func<Vector3, IEnumerator> generate)
        {
            Log($"now: {Calendar.Describe()} (world time {Calendar.Seconds:0} s, day length {Calendar.DayLength:0} s, hour {TownNpc.Hour()})");
            Log("shadowlords: " + Shadowlords.Describe().Replace("\n", "; "));
            var routes = Shadowlords.Routes;
            Log("itinerary: " + string.Join(" ", Enumerable.Range(0, 8).Select(d => $"day{d}=[{string.Join(",", routes.On(d))}]")));
            for (int i = 0; i < 8; i++)
            {
                var site = Moongates.Site(i);
                var at = Moongates.Position(site);
                Log($"moonstone {i}: {site} at {at}, tile {Plugin.U5.Britannia[site.X, site.Y]:x2}");
            }
            var moons = Calendar.Moons;
            Log("moons: " + string.Join(" ", Enumerable.Range(1, 28).Select(d => $"{d}:{moons.Trammel(d)}{moons.Felucca(d)}")));

            // a moonstone moved and put back (global keys through the server)
            var init = Moongates.Site(7);
            WorldKeys.Set(Moongates.SiteKey + 7, MoonstoneSite.Carried);
            yield return new WaitForSeconds(1f);
            Log($"moonstone 7 dug up: {Moongates.Site(7)} (gate up now: {Moongates.GateUpAt(7)})");
            WorldKeys.Set(Moongates.SiteKey + 7, init.Format());
            yield return new WaitForSeconds(1f);
            Log($"moonstone 7 back: {Moongates.Site(7)}");

            // a Shadowlord in Britain
            var britain = Settlements.Get(2);
            yield return generate(britain.Origin);
            int present = Shadowlords.In(2);
            int index = present >= 0 ? present : 1;
            var body = Shadowlords.Spawn(index, 2, Shadowlords.SpawnPoint(britain), false);
            var ai = body?.GetComponent<MonsterAI>();
            Log($"Britain: {Shadowlords.Name(index)} (scheduled here: {present == index}) body {body?.name}, {body?.m_name}, hp {body?.GetHealth():0}/{body?.GetMaxHealth():0}, " +
                $"resident {body?.GetComponent<ResidentMonster>() != null}, view {ai?.m_viewRange}, at {body?.transform.position - britain.Origin}");
            yield return new WaitForSeconds(5f);
            Log($"Britain after 5 s: {(body != null ? "still there" : "gone")} (should be {(present == index ? "there" : "gone")})");
            var folk = UnityEngine.Object.FindObjectsByType<TownNpc>(FindObjectsSortMode.None).Where(n => n.Location == 2).ToList();
            for (int d = 1; d <= 12; d++)
            {
                int sl = routes.In(2, d);
                if (sl != 1 && sl != 2) continue;
                var npcs = Plugin.U5.NpcsOf(Plugin.U5.Location(2));
                int touched = npcs.Count(n => n.Slot > 0 && n.Exists && routes.Affected(d, 2, n.Slot, n.Schedule.Times, npcs[4].Type));
                Log($"Britain day {d}: {ShadowlordRoutes.Airs[sl]} touches {touched} of {npcs.Count(n => n.Slot > 0 && n.Exists)} townsfolk");
            }
            Log($"Britain today: {folk.Count} townsfolk, swayed {folk.Count(f => f.Swayed != 0)}; duties {string.Join(", ", folk.GroupBy(f => f.Duty).Select(g => $"{g.Key} {g.Count()}"))}");

            // a Shadowlord called to the Flame of Truth (the Lycaeum) and destroyed with its shard
            var site0 = Shadowlords.ShardSite(0);
            var lycaeum = Settlements.Get(30);
            if (site0 == null || lycaeum == null) { Log("the Lycaeum is not built"); yield break; }
            yield return generate(site0.Value);
            var called = Shadowlords.Spawn(0, 30, site0.Value + Vector3.forward * 2f * lycaeum.Plan.Tile + Vector3.up * 0.3f, true);
            yield return new WaitForSeconds(3f);
            Log($"Lycaeum: shard site {site0.Value - lycaeum.Origin}, {Shadowlords.Name(0)} called: {called != null}, at {called?.transform.position - site0.Value} from the site");
            bool wrongShard = Shadowlords.CastIntoFlame(1, site0.Value);
            bool doom = Shadowlords.CastIntoFlame(0, site0.Value);
            yield return new WaitForSeconds(2f);
            Log($"Lycaeum: the wrong shard dooms {wrongShard}; Falsehood's dooms {doom}; doomed {Shadowlords.Doomed(0)}; body gone {called == null}; Faulinei now {Shadowlords.TownOf(0)}");
            ZoneSystem.instance.RemoveGlobalKey(Shadowlords.DoomKey + 0);
            yield return new WaitForSeconds(1f);
            Log($"doom taken back: doomed {Shadowlords.Doomed(0)}");
        }
    }
}
