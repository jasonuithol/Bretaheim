using System.Collections;
using System.Linq;
using U5InValheim.WorldGen;
using U5InValheim.WorldGen.Core;
using UnityEngine;

namespace U5InValheim.DevTools
{
    /// <summary>
    /// Development self-test on a dedicated server (which generates no zones until a player
    /// joins): spawns Britain's townsfolk once the world is up, logs who they are and what they
    /// wear, and deletes them again (with no player near, the server unloads them at once; how
    /// they walk is tested offline, TownsfolkTests).
    /// </summary>
    public class TownsfolkSelfTest : MonoBehaviour
    {
        static bool holding;

        void Awake()
        {
            var remove = HarmonyLib.AccessTools.Method(typeof(ZNetScene), "RemoveObjects");
            if (remove != null) new HarmonyLib.Harmony(Plugin.PluginGUID + ".selftest").Patch(remove, prefix: new HarmonyLib.HarmonyMethod(typeof(TownsfolkSelfTest), nameof(Hold)));
        }

        static bool Hold() => !holding;

        IEnumerator Start()
        {
            while (ZNet.instance == null || !ZNet.instance.IsDedicated() || ZNetScene.instance == null || Settlements.Get(2) == null || EnvMan.instance == null || ZoneSystem.instance == null)
                yield return new WaitForSeconds(1f);
            yield return new WaitForSeconds(5f);
            holding = true; // (a headless server unloads everything with no player near: not during the test)
            var b = Settlements.Get(2);
            // Britain's ground, pieces and people, as a player arriving would make them
            yield return Generate(b.Origin);
            var folk = FindObjectsByType<TownNpc>(FindObjectsSortMode.None).Where(n => n.Location == 2).OrderBy(n => n.GetHoverName()).ToList();
            var zdos = folk.Select(n => n.GetComponent<ZNetView>().GetZDO()).ToList();
            Plugin.Log.LogInfo($"townsfolk self-test hour {TownNpc.Hour()}: {folk.Count} people in Britain");
            // walking under physics: nobody falls through the world, people get where they are going
            var start = folk.ToDictionary(f => f, f => f.transform.position);
            yield return new WaitForSeconds(10f);
            Plugin.Log.LogInfo("townsfolk self-test walk 10s: " + string.Join("; ", folk.Where(f => f != null).Take(14).Select(f =>
            {
                var p = f.transform.position - b.Origin;
                var hum = f.GetComponent<Humanoid>();
                return $"{f.GetHoverName()} moved {Vector3.Distance(start[f], f.transform.position):0.0} m, y {p.y:0.0}, floor {f.Floor}, hp {f.Health:0}/{f.MaxHealth:0}, wears {hum.GetInventory().GetEquippedItems().Count}";
            })));
            // Iolo's Bows' stock as the trade window gets it
            var keeper = folk.FirstOrDefault(f => f.GetHoverName() == "Armourer");
            var trader = keeper?.ShopTrader();
            Plugin.Log.LogInfo($"townsfolk self-test shop: {(trader == null ? "none" : string.Join("; ", trader.m_items.Select(t => $"{t.m_prefab?.name} x{t.m_stack} {t.m_price} gp")))}");
            // the town's alarm: a monster on the street beside a villager, away from the guards
            yield return Alarm(b, folk, 32, 40, false, zdos);  // an orc
            yield return Alarm(b, folk, 41, 60, false, zdos);  // a troll
            yield return Alarm(b, folk, 32, 30, true, zdos);   // an orc, the guards down
            // the original's random encounters, just outside Britain (the turn's roll forced)
            var rnd = new System.Random(5);
            var outside = b.Origin + new Vector3(b.Site.Half + 70f, 0, 0);
            yield return Generate(outside);
            for (int i = 0; i < 6; i++)
            {
                var said = UltimaSpawner.Encounter(outside, "the self-test", rnd);
                Plugin.Log.LogInfo($"townsfolk self-test encounter: {said ?? "none"}");
            }
            foreach (var c in Character.GetAllCharacters().Where(c => c.GetComponent<WildEncounter>() != null).Take(6))
                Plugin.Log.LogInfo($"townsfolk self-test encounter: {c.m_name} hp {c.GetHealth():0}/{c.GetMaxHealth():0} group {c.m_group} level {c.GetLevel()}");
            // North Britanny has no guards: its villager runs to Lord British's castle for help
            var north = Settlements.Get(20);
            if (north != null)
            {
                yield return Generate(north.Origin);
                var northFolk = FindObjectsByType<TownNpc>(FindObjectsSortMode.None).Where(n => n.Location == 20).ToList();
                yield return Alarm(north, northFolk, 32, 45, false, zdos);
            }
            holding = false;
        }

        /// <summary>Generates the zones round a point (ground, settlement pieces, townsfolk) as a player arriving would.</summary>
        IEnumerator Generate(Vector3 at)
        {
            var zs = HarmonyLib.Traverse.Create(ZoneSystem.instance);
            for (int i = 0; i < 40; i++)
            {
                bool made = zs.Method("CreateLocalZones", at).GetValue<bool>();
                yield return null;
                if (!made && i > 2) break;
            }
            yield return new WaitForSeconds(3f);
        }

        /// <summary>A monster on the street beside a villager (away from the guards), and what the town does, second by second.</summary>
        IEnumerator Alarm(Settlements.Built b, System.Collections.Generic.List<TownNpc> folk, int type, int seconds, bool guardsDown, System.Collections.Generic.List<ZDO> zdos)
        {
            string monster = Plugin.U5.Ovl.CreatureName(type);
            var guards = folk.Where(f => f != null && f.IsGuard).ToList();
            var villager = folk.Where(f => f != null && !f.IsGuard && f.Floor == 0)
                .OrderByDescending(f => guards.Count == 0 ? 0 : guards.Min(g => Vector3.Distance(g.transform.position, f.transform.position))).FirstOrDefault();
            if (villager == null) { Plugin.Log.LogWarning($"townsfolk self-test alarm: no villager in {b.Location.Name}"); yield break; }
            var vl = villager.transform.position - b.Origin;
            int vc = b.Plan.CellAt(vl.x, vl.z);
            int spot = Enumerable.Range(0, 1024).Where(c => b.Plan.Walk[0][c] && Mathf.Max(Mathf.Abs(c % 32 - vc % 32), Mathf.Abs(c / 32 - vc / 32)) is int d && d >= 2 && d <= 3).FirstOrDefault();
            var (sx, _, sz) = b.Plan.TilePos(0, spot);
            var m = UltimaCreatures.Spawn(type, b.Origin + new Vector3(sx, 0.2f, sz), false);
            zdos.Add(m.GetComponent<ZNetView>().GetZDO());
            // all the guards down: reinforcements should come
            if (guardsDown) foreach (var g in guards) g.Hurt(100000f);
            float t0 = Time.time;
            m.m_onDeath += () => Plugin.Log.LogInfo($"townsfolk self-test alarm: the {monster} died at {Time.time - t0:0.0}s");
            Plugin.Log.LogInfo($"townsfolk self-test alarm: a {monster} ({m.GetMaxHealth():0} hp) beside {villager.GetHoverName()} in {b.Location.Name}");
            for (int i = 0; i < seconds; i++)
            {
                yield return new WaitForSeconds(1f);
                var all = FindObjectsByType<TownNpc>(FindObjectsSortMode.None).Where(f => f.Location == b.Location.Number).ToList();
                var duties = all.GroupBy(f => f.IsReinforcement ? "Reinforcement " + f.Duty : f.Duty.ToString()).Select(g => $"{g.Key} {g.Count()}");
                var hurt = all.Where(f => !f.IsDead && f.Health < f.MaxHealth).Select(f => $"{f.GetHoverName()} {f.Health:0}");
                var aim = m != null ? HarmonyLib.Traverse.Create(m.GetComponent<MonsterAI>()).Field("m_targetCreature").GetValue<Character>() : null;
                var fallen = all.Where(f => f.IsDead).Select(f => f.GetHoverName());
                if (m != null)
                {
                    var ml = m.transform.position - b.Origin;
                    int mc = b.Plan.CellAt(ml.x, ml.z);
                    Plugin.Log.LogInfo($"townsfolk self-test chase t={i + 1}s: {monster} at ({mc % 32},{mc / 32}) y {ml.y:0.0}; fighters " + string.Join(" ", all.Where(f => f.Duty == Duty.Fight).Select(f =>
                    {
                        var fl = f.transform.position - b.Origin;
                        int fc = b.Plan.CellAt(fl.x, fl.z);
                        return $"({fc % 32},{fc / 32}) y {fl.y:0.0} {Vector3.Distance(f.transform.position, m.transform.position):0.0}m v {f.GetComponent<Rigidbody>().linearVelocity.magnitude:0.0}";
                    })));
                }
                Plugin.Log.LogInfo($"townsfolk self-test alarm t={i + 1}s: {string.Join(", ", duties)}; {monster} {(m != null ? $"{m.GetHealth():0} hp, after {(aim != null ? aim.m_name : "nobody")}" : "gone")}{(hurt.Any() ? $"; hurt: {string.Join(" ", hurt)}" : "")}{(fallen.Any() ? $"; fallen: {string.Join(", ", fallen)}" : "")}");
                if (m == null && all.All(f => f.Duty == Duty.None)) break;
            }
        }

        static string Name(int hash) => hash == 0 ? "-" : ObjectDB.instance?.GetItemPrefab(hash)?.name ?? ZNetScene.instance?.GetPrefab(hash)?.name ?? hash.ToString();
    }
}
