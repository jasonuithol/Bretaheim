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
            // U5_SELFTEST=crime: only the ballista, arrest and jail scenarios
            if (System.Environment.GetEnvironmentVariable("U5_SELFTEST") == "crime")
            {
                yield return Crimes();
                holding = false;
                yield break;
            }
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
            // what the player's hover finds on a townsperson (the first Hoverable on the body)
            var hov = folk.First(f => f != null).GetComponentInParent<Hoverable>();
            Plugin.Log.LogInfo($"townsfolk self-test hover: {hov.GetType().Name} says \"{hov.GetHoverText().Replace("\n", " | ")}\" ({hov.GetHoverName()})");
            // how smoothly they walk: samples of walking bodies that have stalled, and teleports
            int samples = 0, stalls = 0, teleports = TownNpc.Teleports;
            for (float t = 0; t < 15f; t += 0.1f)
            {
                yield return new WaitForSeconds(0.1f);
                foreach (var f in folk.Where(f => f != null && f.Walking))
                {
                    var v = f.GetComponent<Rigidbody>().linearVelocity;
                    v.y = 0;
                    samples++;
                    if (v.magnitude < 0.5f) stalls++;
                }
            }
            Plugin.Log.LogInfo($"townsfolk self-test smooth: {stalls}/{samples} walking samples stalled, {TownNpc.Teleports - teleports} teleports in 15 s");
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
            // a Valheim monster made the plain way (as the spawn command does) is the Ultima creature of its body
            var plain = Instantiate(ZNetScene.instance.GetPrefab("Troll"), outside + Vector3.up * 2, Quaternion.identity).GetComponent<Character>();
            plain.SetLevel(2);
            var plainStats = UltimaCreatures.Of(plain);
            Plugin.Log.LogInfo($"townsfolk self-test plain troll: {(plainStats == null ? "not Ultima" : Plugin.U5.Ovl.CreatureName(plainStats.Type))} hp {plain.GetHealth():0}/{plain.GetMaxHealth():0} group {plain.m_group}");
            // North Britanny has no guards: its villager runs to Lord British's castle for help
            var north = Settlements.Get(20);
            if (north != null)
            {
                yield return Generate(north.Origin);
                var northFolk = FindObjectsByType<TownNpc>(FindObjectsSortMode.None).Where(n => n.Location == 20).ToList();
                yield return Alarm(north, northFolk, 32, 45, false, zdos);
            }
            yield return Crimes();
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

        /// <summary>
        /// Lord British's ballistas against an orc outside the walls; a player (a stand-in body)
        /// wanted there, then fighting the guards; the jail of Yew.
        /// </summary>
        IEnumerator Crimes()
        {
            var castle = Settlements.Get(17);
            if (castle == null) { Plugin.Log.LogWarning("townsfolk self-test crime: the castle is not built"); yield break; }
            yield return Generate(castle.Origin);
            yield return new WaitForSeconds(3f);
            var ballistas = FindObjectsByType<TownBallista>(FindObjectsSortMode.None).Where(t => Vector3.Distance(t.transform.position, castle.Origin) < 60f).ToList();
            Plugin.Log.LogInfo($"townsfolk self-test ballista: {ballistas.Count} ballistas at the castle");
            // the one facing south over the gate: an orc 20 m in front of it, outside the walls
            var gate = ballistas.OrderBy(t => (t.transform.position - castle.Origin).z).FirstOrDefault();
            if (gate != null)
            {
                var at = gate.transform.position + gate.transform.forward * 22f;
                at.y = ZoneSystem.instance.GetGroundHeight(at) + 0.2f;
                var orc = UltimaCreatures.Spawn(32, at, false);
                var t0 = Time.time;
                bool dead = false;
                orc.m_onDeath += () => { dead = true; Plugin.Log.LogInfo($"townsfolk self-test ballista: the orc died at {Time.time - t0:0.0}s"); };
                Plugin.Log.LogInfo($"townsfolk self-test ballista: an orc ({orc.GetMaxHealth():0} hp) {Vector3.Distance(at, gate.transform.position):0} m before the ballista at {gate.transform.position - castle.Origin}, facing {gate.transform.forward}");
                for (int i = 0; i < 30 && !dead; i++)
                {
                    yield return new WaitForSeconds(1f);
                    var crew = FindObjectsByType<TownNpc>(FindObjectsSortMode.None).Where(n => n.Duty == Duty.Operate).ToList();
                    foreach (var bt in ballistas.Where(x => HarmonyLib.Traverse.Create(x).Field("target").GetValue<Character>() != null))
                    {
                        var tt = HarmonyLib.Traverse.Create(bt.GetComponent<Turret>());
                        Plugin.Log.LogInfo($"townsfolk self-test ballista at {bt.transform.position - castle.Origin}: turret target {tt.Field("m_target").GetValue<Character>()?.m_name ?? "none"}, aim {tt.Field("m_aimDiffToTarget").GetValue<float>():0.00}, cooling {bt.GetComponent<Turret>().IsCoolingDown()}, manned {HarmonyLib.Traverse.Create(bt).Property("Manned").GetValue<bool>()}");
                    }
                    Plugin.Log.LogInfo($"townsfolk self-test ballista t={i + 1}s: crew {crew.Count}{string.Join("", crew.Select(c => $" ({Vector3.Distance(c.transform.position, gate.transform.position):0.0} m from it)"))}; orc {(orc != null ? $"{orc.GetHealth():0} hp at {Vector3.Distance(orc.transform.position, gate.transform.position):0} m" : "gone")}");
                }
                for (int i = 0; i < 20; i++) yield return new WaitForSeconds(1f);
                Plugin.Log.LogInfo($"townsfolk self-test ballista: crew after the fight {FindObjectsByType<TownNpc>(FindObjectsSortMode.None).Count(n => n.Duty == Duty.Operate)}");
            }
            // a stand-in player in the courtyard, wanted: two guards come to arrest them
            var body = Instantiate(ZNetScene.instance.GetPrefab("Player"), castle.Origin + new Vector3(0, 0.3f, -6f), Quaternion.identity).GetComponent<Player>();
            // (a body, not a player: an owner destroys every Player but its local one, and a server has no camera)
            body.enabled = false;
            var zdo = body.GetComponent<ZNetView>().GetZDO();
            zdo.Set("u5_wanted", 17);
            yield return new WaitForSeconds(1f);
            var cf = FindObjectsByType<TownNpc>(FindObjectsSortMode.None).Where(n => n.Location == 17).ToList();
            Plugin.Log.LogInfo($"townsfolk self-test arrest: wanted {Crime.WantedBy(body)}, players {Player.GetAllPlayers().Count}, castle folk {cf.Count} (owned {cf.Count(n => n.IsOwned)}, guards {cf.Count(n => n.IsGuard)}, duties {string.Join(",", cf.Select(n => n.Duty))}), body at {body.transform.position - castle.Origin}");
            for (int i = 0; i < 25; i++)
            {
                yield return new WaitForSeconds(1f);
                var coming = FindObjectsByType<TownNpc>(FindObjectsSortMode.None).Where(n => n.Duty == Duty.Arrest).ToList();
                Plugin.Log.LogInfo($"townsfolk self-test arrest t={i + 1}s: {coming.Count} guards coming, nearest {(coming.Count > 0 ? coming.Min(n => Vector3.Distance(n.transform.position, body.transform.position)) : -1):0.0} m");
                if (coming.Any(n => Vector3.Distance(n.transform.position, body.transform.position) <= Crime.Reach)) { Plugin.Log.LogInfo($"townsfolk self-test arrest: a guard reached them at {i + 1}s"); break; }
            }
            Plugin.Log.LogInfo($"townsfolk self-test arrest: challenge \"{Crime.Challenge.Replace("\n", " ")}\"");
            // they would not come quietly: the guards fight them
            zdo.Set("u5_wanted", 0);
            zdo.Set("u5_hostile", 17);
            float hp0 = body.GetHealth();
            var aGuard = FindObjectsByType<TownNpc>(FindObjectsSortMode.None).First(n => n.IsGuard && n.Location == 17 && !n.IsReinforcement).GetComponent<Character>();
            Plugin.Log.LogInfo($"townsfolk self-test rogue: hostile {Crime.HostileTo(body)}, a guard and they are enemies: {BaseAI.IsEnemy(aGuard, body)} / {BaseAI.IsEnemy(body, aGuard)}");
            for (int i = 0; i < 20; i++)
            {
                yield return new WaitForSeconds(1f);
                var fighting = FindObjectsByType<TownNpc>(FindObjectsSortMode.None).Count(n => n.Duty == Duty.Fight);
                Plugin.Log.LogInfo($"townsfolk self-test rogue t={i + 1}s: {fighting} guards fighting, health {body.GetHealth():0}/{hp0:0}");
                if (body.GetHealth() < hp0) { Plugin.Log.LogInfo($"townsfolk self-test rogue: a guard's blow landed at {i + 1}s"); break; }
            }
            zdo.Set("u5_hostile", 0);
            ZNetScene.instance.Destroy(body.gameObject);
            // the jail of Yew
            var yew = Settlements.Get(Crime.JailLocation);
            if (yew == null) { Plugin.Log.LogWarning("townsfolk self-test jail: Yew is not built"); yield break; }
            yield return Generate(yew.Origin);
            var (jx, jy, jz) = yew.Plan.TilePos(0, Crime.JailY * SettlementPlan.Size + Crime.JailX);
            var cell = yew.Origin + new Vector3(jx, jy, jz);
            bool floor = Physics.Raycast(cell + Vector3.up * 1.5f, Vector3.down, out var hit, 5f);
            var walls = new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right }.Select(d => Physics.Raycast(cell + Vector3.up * 1.2f, d, out var w, 6f) ? $"{w.distance:0.0}m {w.collider.name}" : "open");
            Plugin.Log.LogInfo($"townsfolk self-test jail: Yew's cell at {cell} ({yew.Location.Name}, {FindObjectsByType<TownNpc>(FindObjectsSortMode.None).Count(n => n.Location == 4)} townsfolk); floor {(floor ? $"{hit.collider.name} at {hit.point.y - cell.y:0.00}" : "none")}; walls N S W E: {string.Join(", ", walls)}");
        }

        static string Name(int hash) => hash == 0 ? "-" : ObjectDB.instance?.GetItemPrefab(hash)?.name ?? ZNetScene.instance?.GetPrefab(hash)?.name ?? hash.ToString();
    }
}
