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
            // U5_SELFTEST=town:<location>: only that settlement, built and lived in for a while
            var only = System.Environment.GetEnvironmentVariable("U5_SELFTEST") ?? "";
            if (only.StartsWith("town:"))
            {
                var list = only.Substring(5).Split(',').Select(int.Parse).ToList();
                foreach (int n in list) yield return Town(n, list.Count > 1 ? 1 : 3);
                Done();
                yield break;
            }
            // U5_SELFTEST=sleep: inns and camps
            if (only == "sleep")
            {
                yield return Sleeping();
                Done();
                yield break;
            }
            // U5_SELFTEST=hut: Iolo's hut (Hildir and the lock), the restrictions, the introduction's files
            if (only == "hut")
            {
                yield return HutSelfTest.Run(Generate);
                Done();
                yield break;
            }
            // U5_SELFTEST=crime: only the ballista, arrest and jail scenarios
            if (only == "crime")
            {
                yield return Crimes();
                Done();
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
            Done();
        }

        /// <summary>
        /// The checks are over: a server started with U5_SELFTEST (town:…, sleep, crime, or any other
        /// value for the whole test) shuts down, saving its world; one with only the SelfTest setting stays up.
        /// </summary>
        static void Done()
        {
            holding = false;
            Plugin.Log.LogInfo("townsfolk self-test done");
            if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("U5_SELFTEST"))) return;
            Plugin.Log.LogInfo("townsfolk self-test: shutting the server down");
            Application.Quit();
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
            // the ettin: its own prefab (spawn ettin), two heads, the Ettin's numbers
            var ettinPrefab = ZNetScene.instance.GetPrefab(Ettin.PrefabName);
            if (ettinPrefab == null) Plugin.Log.LogWarning("townsfolk self-test ettin: no Ettin prefab");
            else
            {
                var ettin = Instantiate(ettinPrefab, castle.Origin + new Vector3(0, 2f, -70f), Quaternion.identity).GetComponent<Character>();
                yield return new WaitForSeconds(1f);
                var es = UltimaCreatures.Of(ettin);
                var second = ettin.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "u5_second_head");
                if (second != null)
                {
                    var rs = second.GetComponentsInChildren<Renderer>(true);
                    string Bx(Bounds b) => $"centre {ettin.transform.InverseTransformPoint(b.center)}, size {b.size}";
                    Plugin.Log.LogInfo($"townsfolk self-test ettin head: active {second.gameObject.activeInHierarchy}, {rs.Length} renderers ({string.Join(", ", rs.Select(r => $"{r.name} enabled {r.enabled} {r.GetType().Name} {Bx(r.bounds)}"))}); scale {second.lossyScale}");
                    foreach (var smr in ettin.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        Plugin.Log.LogInfo($"townsfolk self-test ettin body: {smr.name} {Bx(smr.bounds)}");
                    var headBone = second.parent;
                    Plugin.Log.LogInfo($"townsfolk self-test ettin bones: head {ettin.transform.InverseTransformPoint(headBone.position)}, neck {(headBone.parent != null ? ettin.transform.InverseTransformPoint(headBone.parent.position).ToString() : "-")}");
                }
                Plugin.Log.LogInfo($"townsfolk self-test ettin: {ettin.m_name} ({(es == null ? "not Ultima" : Plugin.U5.Ovl.CreatureName(es.Type))}) hp {ettin.GetHealth():0}/{ettin.GetMaxHealth():0}, second head {ettin.GetComponentsInChildren<Transform>(true).Any(t => t.name == "u5_second_head")}, red look {ettin.GetComponent<EttinLook>() != null}");
                // it falls: its body is the ettin's ragdoll (two heads, red), not the troll's
                ettin.SetHealth(0);
                ettin.GetType().GetMethod("CheckDeath", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(ettin, null);
                yield return new WaitForSeconds(1.5f);
                var corpse = FindObjectsByType<Ragdoll>(FindObjectsSortMode.None).Where(r => Vector3.Distance(r.transform.position, castle.Origin + new Vector3(0, 2f, -70f)) < 15f).ToList();
                Plugin.Log.LogInfo($"townsfolk self-test ettin: fallen, ragdolls {string.Join(", ", corpse.Select(r => $"{r.name} (second head {r.GetComponentsInChildren<Transform>(true).Any(t => t.name == "u5_second_head")}, red look {r.GetComponent<EttinLook>() != null})"))}");
                if (ettin != null) ZNetScene.instance.Destroy(ettin.gameObject);
            }
            // the gypsy's sphere (tile 0x29): a Moder tear on a stand on a round table
            foreach (var st in FindObjectsByType<ItemStand>(FindObjectsSortMode.None).Where(t => Vector3.Distance(t.transform.position, castle.Origin) < 60f))
            {
                var held = HarmonyLib.Traverse.Create(st).Field("m_visualItem").GetValue<GameObject>();
                bool table = Physics.RaycastAll(st.transform.position + Vector3.up * 0.3f, Vector3.down, 1f).Any(h => h.collider.GetComponentInParent<Piece>()?.name.StartsWith("piece_table_round") == true);
                Plugin.Log.LogInfo($"townsfolk self-test sphere: stand {st.name} at {st.transform.position - castle.Origin} holds {Name(st.GetComponent<ZNetView>().GetZDO().GetInt(ZDOVars.s_item))} (shown {(held != null ? held.name : "nothing")}), on a round table {table}, removable {st.m_canBeRemoved}");
            }
            var chains = FindObjectsByType<ZNetView>(FindObjectsSortMode.None).Where(v => v.name.StartsWith("dvergrprops_hooknchain") && Vector3.Distance(v.transform.position, castle.Origin) < 60f).ToList();
            Plugin.Log.LogInfo($"townsfolk self-test shackles: {chains.Count} hanging chains at the castle{string.Join("", chains.Take(3).Select(v => $"; at {v.transform.position - castle.Origin} scale {v.transform.localScale.y:0.0}, {string.Join(",", v.GetComponents<Component>().Select(c => c.GetType().Name))}"))}");
            var onHit = HarmonyLib.AccessTools.Method(typeof(Projectile), "OnHit");
            new HarmonyLib.Harmony("u5.selftest.bolts").Patch(onHit, prefix: new HarmonyLib.HarmonyMethod(typeof(TownsfolkSelfTest), nameof(BoltHit)));
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
            // upstairs: the guards climb to them, and strike only on their storey
            body.SetMaxHealth(1e6f);
            body.SetHealth(1e6f);
            var plan = castle.Plan;
            var walk1 = plan.Walk.TryGetValue(1, out var w1) ? w1 : null;
            // (a floor-1 tile the guards can reach by the ladders, from where they stand in the courtyard)
            int yard = plan.CellAt(0, -6f);
            int up = walk1 == null ? -1 : Enumerable.Range(0, walk1.Length).Where(c => walk1[c] && plan.Walk[0][c] && TownPath.Find(plan, 0, yard, 1, c) != null)
                .OrderBy(c => { var (px, _, pz) = plan.TilePos(1, c); return new Vector2(px, pz + 6f).magnitude; }).DefaultIfEmpty(-1).First();
            if (up >= 0)
            {
                var (ux, uy, uz) = plan.TilePos(1, up);
                body.transform.position = castle.Origin + new Vector3(ux, uy + 0.3f, uz);
                body.GetComponent<Rigidbody>().position = body.transform.position;
                float hp1 = body.GetHealth();
                Plugin.Log.LogInfo($"townsfolk self-test rogue upstairs: on floor 1 at tile ({up % 32},{up / 32}), {body.transform.position - castle.Origin}");
                for (int i = 0; i < 45; i++)
                {
                    yield return new WaitForSeconds(1f);
                    var near = FindObjectsByType<TownNpc>(FindObjectsSortMode.None).Where(n => n.Duty == Duty.Fight).OrderBy(n => Vector3.Distance(n.transform.position, body.transform.position)).FirstOrDefault();
                    var d = near != null ? near.transform.position - body.transform.position : Vector3.zero;
                    if (i % 3 == 2 || body.GetHealth() < hp1)
                        Plugin.Log.LogInfo($"townsfolk self-test rogue upstairs t={i + 1}s: nearest fighting guard {(near != null ? $"floor {near.Floor}, {new Vector2(d.x, d.z).magnitude:0.0} m across, {d.y:0.0} m up" : "none")}; health {body.GetHealth():0}");
                    if (body.GetHealth() < hp1) { Plugin.Log.LogInfo($"townsfolk self-test rogue upstairs: a blow landed at {i + 1}s from {d.y:0.0} m up"); break; }
                }
            }
            // a minute's fight: a guard appears beside them and takes them to Yew
            var take = HarmonyLib.AccessTools.Method(typeof(Crime), "Overpowered");
            var before = FindObjectsByType<TownNpc>(FindObjectsSortMode.None).Length;
            Plugin.Instance.StartCoroutine((IEnumerator)take.Invoke(null, new object[] { body, castle }));
            yield return new WaitForSeconds(0.5f);
            var captor = FindObjectsByType<TownNpc>(FindObjectsSortMode.None).Where(n => n.IsReinforcement && n.IsGuard && n.Duty == Duty.Arrest).OrderBy(n => Vector3.Distance(n.transform.position, body.transform.position)).FirstOrDefault();
            Plugin.Log.LogInfo($"townsfolk self-test overpowered: hostile {Crime.HostileTo(body)}, a guard appeared {(captor != null ? $"{Vector3.Distance(captor.transform.position, body.transform.position):0.0} m from them" : "no")}");
            yield return new WaitForSeconds(9f);
            Plugin.Log.LogInfo($"townsfolk self-test overpowered: the guard gone {captor == null}, they are at {body.transform.position - castle.Origin}");
            zdo.Set("u5_hostile", 0);
            ZNetScene.instance.Destroy(body.gameObject);
            // attacking townsfolk: a villager cries for the guards and runs; either way the guards come to arrest
            foreach (bool guardFirst in new[] { false, true })
            {
                var striker = Instantiate(ZNetScene.instance.GetPrefab("Player"), castle.Origin + new Vector3(0, 0.3f, -6f), Quaternion.identity).GetComponent<Player>();
                striker.enabled = false;
                yield return new WaitForSeconds(0.5f);
                var folk = FindObjectsByType<TownNpc>(FindObjectsSortMode.None).Where(n => n.Location == 17 && !n.IsDead && !n.IsReinforcement && n.IsGuard == guardFirst && n.Floor == 0)
                    .OrderBy(n => Vector3.Distance(n.transform.position, striker.transform.position)).FirstOrDefault();
                if (folk == null) { Plugin.Log.LogWarning($"townsfolk self-test assault: no {(guardFirst ? "guard" : "villager")} to strike"); ZNetScene.instance.Destroy(striker.gameObject); continue; }
                var victim = folk.GetComponent<Character>();
                float hpBefore = victim.GetHealth();
                var blow = new HitData { m_damage = new HitData.DamageTypes { m_blunt = 20 }, m_point = victim.GetCenterPoint(), m_dir = Vector3.forward };
                blow.SetAttacker(striker);
                // (the striker plays the local player for the blow: a server has none)
                Player.m_localPlayer = striker;
                try { victim.Damage(blow); }
                finally { Player.m_localPlayer = null; }
                yield return new WaitForSeconds(3f);
                var coming = FindObjectsByType<TownNpc>(FindObjectsSortMode.None).Count(n => n.Location == 17 && n.Duty == Duty.Arrest);
                Plugin.Log.LogInfo($"townsfolk self-test assault: {(guardFirst ? "guard" : "villager")} {folk.GetHoverName()} struck (health {hpBefore:0} -> {victim.GetHealth():0}): wanted {Crime.WantedBy(striker)}, their duty {folk.Duty}, {coming} guards coming to arrest");
                striker.GetComponent<ZNetView>().GetZDO().Set("u5_wanted", 0);
                yield return new WaitForSeconds(2f);
                ZNetScene.instance.Destroy(striker.gameObject);
            }
            // the jail of Yew
            var yew = Settlements.Get(Crime.JailLocation);
            if (yew == null) { Plugin.Log.LogWarning("townsfolk self-test jail: Yew is not built"); yield break; }
            yield return Generate(yew.Origin);
            var (jx, jy, jz) = yew.Plan.TilePos(0, Crime.JailY * SettlementPlan.Size + Crime.JailX);
            var cell = yew.Origin + new Vector3(jx, jy, jz);
            bool floor = Physics.Raycast(cell + Vector3.up * 1.5f, Vector3.down, out var hit, 5f);
            var walls = new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right }.Select(d => Physics.Raycast(cell + Vector3.up * 1.2f, d, out var w, 6f) ? $"{w.distance:0.0}m {w.collider.name}" : "open");
            Plugin.Log.LogInfo($"townsfolk self-test jail: Yew's cell at {cell} ({yew.Location.Name}, {FindObjectsByType<TownNpc>(FindObjectsSortMode.None).Count(n => n.Location == 4)} townsfolk); floor {(floor ? $"{hit.collider.name} at {hit.point.y - cell.y:0.00}" : "none")}; walls N S W E: {string.Join(", ", walls)}");
            yield return Jail(yew, cell);
        }

        /// <summary>Yew's jail: prisoners (not guards) in the cells, the cell door locked, the secret wall behind the cell, the trapdoor and ladders down to the basement.</summary>
        static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        IEnumerator Jail(Settlements.Built yew, Vector3 cell)
        {
            var plan = yew.Plan;
            Vector3 At(int z, int x, int y) { var (px, py, pz) = plan.TilePos(z, y * SettlementPlan.Size + x); return yew.Origin + new Vector3(px, py, pz); }
            yield return new WaitForSeconds(2f);
            var cells = plan.Cells[0];
            Plugin.Log.LogInfo($"townsfolk self-test jail: cells {cells.Count(c => c)} tiles");
            foreach (var n in FindObjectsByType<TownNpc>(FindObjectsSortMode.None).Where(n => n.Location == 4))
            {
                var local = n.transform.position - yew.Origin;
                int tx = Mathf.RoundToInt((local.x + 31) / 2), ty = Mathf.RoundToInt((31 - local.z) / 2);
                bool inCell = tx >= 0 && ty >= 0 && tx < 32 && ty < 32 && cells[ty * 32 + tx] && local.y > -1f;
                if (!inCell && !n.IsPrisoner) continue;
                var chest = n.GetComponent<Humanoid>().GetInventory().GetEquippedItems().Select(i => i.m_dropPrefab?.name);
                Plugin.Log.LogInfo($"townsfolk self-test jail: {n.GetHoverName()} at tile ({tx},{ty}) in a cell {inCell}, prisoner {n.IsPrisoner}, guard {n.IsGuard}, wearing {string.Join(",", chest)}");
            }
            // a prisoner sent out into the hall stays behind the locked door
            var jerone = FindObjectsByType<TownNpc>(FindObjectsSortMode.None).FirstOrDefault(n => n.Location == 4 && n.IsPrisoner && n.GetHoverName() == "Jerone");
            if (jerone != null)
            {
                jerone.RunTo(0, 8 * 32 + 25);
                yield return new WaitForSeconds(12f);
                var jl = jerone.transform.position - yew.Origin;
                int jx = Mathf.RoundToInt((jl.x + 31) / 2), jy = Mathf.RoundToInt((31 - jl.z) / 2);
                var cd = FindObjectsByType<Door>(FindObjectsSortMode.None).OrderBy(d => Vector3.Distance(d.transform.position, At(0, 25, 6))).First();
                Plugin.Log.LogInfo($"townsfolk self-test jail: Jerone told to run to the hall (a prisoner: refused) is at tile ({jx},{jy}), in his cell {cells[jy * 32 + jx]}, the cell door state {cd.GetComponent<ZNetView>().GetZDO().GetInt(ZDOVars.s_state)}");
                jerone.RunTo(0, 4 * 32 + 25);
                yield return new WaitForSeconds(6f);
                Plugin.Log.LogInfo($"townsfolk self-test jail: Jerone sent across his cell to (25,4): at {jerone.transform.position - At(0, 25, 4)} from it");
                if (false) Plugin.Log.LogInfo($"townsfolk self-test jail: Jerone sent to the hall is at tile ({jx},{jy}), in his cell {cells[jy * 32 + jx]}, the cell door state {cd.GetComponent<ZNetView>().GetZDO().GetInt(ZDOVars.s_state)}");
            }
            // the prisoners keep to their cells (wandering, settling) for a minute and a half
            {
                var prisoners = FindObjectsByType<TownNpc>(FindObjectsSortMode.None).Where(n => n.Location == 4 && n.IsPrisoner).ToList();
                int samples = 0, outside = 0;
                for (int i = 0; i < 90; i++)
                {
                    yield return new WaitForSeconds(1f);
                    foreach (var pr in prisoners)
                    {
                        var pl = pr.transform.position - yew.Origin;
                        int px = Mathf.RoundToInt((pl.x + 31) / 2), py = Mathf.RoundToInt((31 - pl.z) / 2);
                        samples++;
                        if (px < 0 || py < 0 || px >= 32 || py >= 32 || !cells[py * 32 + px]) { outside++; Plugin.Log.LogInfo($"townsfolk self-test jail: {pr.GetHoverName()} out of the cells at ({px},{py})"); }
                    }
                }
                Plugin.Log.LogInfo($"townsfolk self-test jail: prisoners outside their cells {outside}/{samples} samples over 90 s");
            }
            // the guillotine (18,12): the head in the kettle, the axe leaning on it, posts and beam
            {
                var g = At(0, 18, 12);
                Bounds? Of(GameObject go) { var rs = go.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToList(); if (rs.Count == 0) return null; var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b; }
                foreach (var v in FindObjectsByType<ZNetView>(FindObjectsSortMode.None).Where(v => Vector3.Distance(v.transform.position, g) < 2.2f && v.transform.position.y < g.y + 4.5f && v.transform.position.y > g.y - 0.5f))
                {
                    var st = v.GetComponent<ItemStand>();
                    var b = st != null ? Of(st.m_attachOther.gameObject) : Of(v.gameObject);
                    Plugin.Log.LogInfo($"townsfolk self-test guillotine: {v.name}{(st != null ? " holding " + Name(v.GetZDO().GetInt(ZDOVars.s_item)) : "")}: {(b.HasValue ? $"y {b.Value.min.y - g.y:0.00}..{b.Value.max.y - g.y:0.00}, x {b.Value.min.x - g.x:0.00}..{b.Value.max.x - g.x:0.00}, z {b.Value.min.z - g.z:0.00}..{b.Value.max.z - g.z:0.00}" : "nothing shown")}");
                }
                var names = new System.Collections.Generic.List<string>();
                foreach (var kind in new[] { "Hair", "Beard" })
                    for (int n = 0; n <= 40; n++)
                    {
                        var it = ObjectDB.instance.GetItemPrefab(kind + n)?.GetComponent<ItemDrop>();
                        if (it != null) names.Add($"{kind}{n}={it.m_itemData.m_shared.m_name}");
                    }
                Plugin.Log.LogInfo($"townsfolk self-test looks: {string.Join(" ", names)}");
            }
            var landon = Plugin.U5.NpcsOf(yew.Location)[13];
            var ls = landon.Schedule;
            Plugin.Log.LogInfo($"townsfolk self-test jail: Landon's places {string.Join(" ", Enumerable.Range(0, 3).Select(i => $"({ls.X[i]},{ls.Y[i]},{ls.Z[i]}){(plan.Seats.ContainsKey((ls.Z[i], ls.Y[i] * 32 + ls.X[i])) ? " seat" : "")}"))}, walkable {plan.Walk.TryGetValue(-1, out var wb) && wb[1 * 32 + 22]}");
            // the cell's door, shut and locked: a player can't open it
            var doorAt = At(0, 25, 6);
            var door = FindObjectsByType<Door>(FindObjectsSortMode.None).OrderBy(d => Vector3.Distance(d.transform.position, doorAt)).FirstOrDefault();
            var body = Instantiate(ZNetScene.instance.GetPrefab("Player"), cell + Vector3.up * 0.3f, Quaternion.identity).GetComponent<Player>();
            body.enabled = false;
            if (door != null)
            {
                var dz = door.GetComponent<ZNetView>().GetZDO();
                door.Interact(body, false, false);
                yield return new WaitForSeconds(1f);
                Plugin.Log.LogInfo($"townsfolk self-test jail: cell door {Vector3.Distance(door.transform.position, doorAt):0.0} m from (25,6), lock \"{dz.GetString(Settlements.LockKey)}\", state after a player's try {dz.GetInt(ZDOVars.s_state)}, hover \"{door.GetHoverText().Replace("\n", " ")}\" (\"{TownDoors.LockedText("key")}\")");
            }
            // the cell door is a 2 m door under a metre of grate you can talk through
            {
                var dAt = At(0, 25, 6);
                var pieces = FindObjectsByType<ZNetView>(FindObjectsSortMode.None).Where(v => Vector3.Distance(v.transform.position, dAt) < 2.6f && (v.name.StartsWith("wood_door") || v.name.StartsWith("iron_floor"))).ToList();
                Plugin.Log.LogInfo($"townsfolk self-test jail: cell door pieces {string.Join(", ", pieces.Select(v => $"{v.name} at {(v.transform.position - dAt).y:0.0} m, colliders on {v.GetComponentsInChildren<Collider>(true).Count(c => c.enabled)}"))}");
                var cam = At(0, 25, 8) + Vector3.up * 2.4f;
                var who = FindObjectsByType<TownNpc>(FindObjectsSortMode.None).Where(n => n.IsPrisoner && n.Location == 4).OrderBy(n => Vector3.Distance(n.transform.position, dAt)).FirstOrDefault();
                if (who != null)
                {
                    var target = who.GetComponent<Character>().GetEyePoint();
                    var hits = Physics.RaycastAll(cam, target - cam, Vector3.Distance(cam, target) + 0.3f, LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "character", "character_net", "character_noenv", "vehicle")).OrderBy(h => h.distance).ToList();
                    Plugin.Log.LogInfo($"townsfolk self-test jail: looking from the hall (camera 2.4 m up) at {who.GetHoverName()}'s head {Vector3.Distance(cam, target):0.0} m away, first hit {(hits.Count > 0 ? hits[0].collider.transform.root.name : "nothing")}");
                }
            }
            // the secret wall behind the cell (25,2): rune-marked bricks; a blow shows the door
            var wallAt = At(0, 25, 2);
            var secret = FindObjectsByType<WearNTear>(FindObjectsSortMode.None).Where(w => Vector3.Distance(w.transform.position, wallAt) < 3f && w.GetComponent<ZNetView>()?.GetZDO()?.GetInt(TownDoors.SecretKey) != 0).ToList();
            var hidden = FindObjectsByType<HiddenDoor>(FindObjectsSortMode.None).Where(h => Vector3.Distance(h.transform.position, wallAt) < 3f).ToList();
            Plugin.Log.LogInfo($"townsfolk self-test jail: secret wall at (25,2): {secret.Count} bricks ({secret.Count(w => w.GetComponent<U5Look>() != null)} rune-marked), {hidden.Count} hidden door(s), the door's collider on {hidden.Select(h => h.GetComponentsInChildren<Collider>(true).Count(c => c.enabled)).FirstOrDefault()}");
            if (secret.Count > 0)
            {
                var hd = hidden.FirstOrDefault()?.gameObject;
                // (as a blow from the local player does; a server has none)
                int took = TownDoors.Reveal(secret[0].transform.position, secret[0].GetComponent<ZNetView>().GetZDO().GetInt(TownDoors.SecretKey));
                yield return new WaitForSeconds(1f);
                Plugin.Log.LogInfo($"townsfolk self-test jail: the blow took {took} bricks away; {FindObjectsByType<WearNTear>(FindObjectsSortMode.None).Count(w => Vector3.Distance(w.transform.position, wallAt) < 3f && w.GetComponent<ZNetView>()?.GetZDO()?.GetInt(TownDoors.SecretKey) != 0)} left; the door shown {(hd != null && hd.GetComponent<HiddenDoor>() == null && hd.GetComponentsInChildren<Renderer>(true).Any(r => r.enabled))}; a way through at 1.5 m: {!Physics.Raycast(At(0, 25, 3) + Vector3.up * 0.8f, Vector3.forward, 4f, LayerMask.GetMask("Default", "static_solid", "piece", "terrain")) || hd != null}");
            }
            ZNetScene.instance.Destroy(body.gameObject);
            // the trapdoor in the hall (25,8) and the corridor's (20,1): a player falls through to the basement
            var solid = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain");
            foreach (var (x, y) in new[] { (25, 8), (20, 1), (24, 8) })
            {
                var top = At(0, x, y) + Vector3.up * 2f;
                bool land = Physics.Raycast(top, Vector3.down, out var h, 12f, solid);
                Plugin.Log.LogInfo($"townsfolk self-test jail: falling at ({x},{y}) lands on {(land ? $"{h.collider.name} {h.point.y - yew.Origin.y:0.0} m" : "nothing")}");
            }
            foreach (var cp in FindObjectsByType<ClimbPoint>(FindObjectsSortMode.None).Where(c => Vector3.Distance(c.transform.position, yew.Origin) < 50f && c.name.StartsWith("iron_floor")))
                Plugin.Log.LogInfo($"townsfolk self-test jail: grate at {cp.transform.position - yew.Origin} ({cp.Verb}) to {cp.Target - yew.Origin}");
            var grate = At(0, 25, 9);
            var under = Physics.RaycastAll(grate + Vector3.up * 1f, Vector3.down, 8f, solid).OrderBy(h => h.distance).Select(h => $"{h.collider.transform.root.name} {h.point.y - yew.Origin.y:0.0}");
            Plugin.Log.LogInfo($"townsfolk self-test jail: down through the grate (25,9): {string.Join(", ", under)}; ladders back up {FindObjectsByType<ClimbPoint>(FindObjectsSortMode.None).Count(c => Flat(c.transform.position, grate) < 2.5f && c.Verb == "Climb up")} pieces");
            var baked = FindObjectsByType<U5Ladder>(FindObjectsSortMode.None).Where(l => Vector3.Distance(l.transform.position, yew.Origin) < 50f).ToList();
            foreach (var l in baked)
            {
                // the ends against the storey lines: an up ladder's top below the floor above's
                // surface, a down ladder's foot above the underside of its floor (wood_floor, 0.13 m)
                var bounds = l.GetComponentsInChildren<Collider>().Select(c => c.bounds).Aggregate((a, b2) => { a.Encapsulate(b2); return a; });
                var r = l.GetComponentsInChildren<Renderer>().Select(c => c.bounds).Aggregate((a, b2) => { a.Encapsulate(b2); return a; });
                float lo = bounds.min.y - yew.Origin.y, hi = bounds.max.y - yew.Origin.y;
                Plugin.Log.LogInfo($"townsfolk self-test jail: ladder {Utils.GetPrefabName(l.gameObject)} at {l.transform.position - yew.Origin}: colliders {lo:0.00}..{hi:0.00}, drawn {r.min.y - yew.Origin.y:0.00}..{r.max.y - yew.Origin.y:0.00}");
            }
            Plugin.Log.LogInfo($"townsfolk self-test jail: {baked.Count} baked ladders in Yew, {baked.Count(l => l.GetComponent<ClimbPoint>() != null)} climbable, {baked.Sum(l => l.GetComponentsInChildren<Collider>().Length)} colliders, layer {(baked.Count > 0 ? LayerMask.LayerToName(baked[0].GetComponentInChildren<Collider>().gameObject.layer) : "-")}");
            // the torture table in the hall (21,8): the axe and knife flat on it, blood on the top, the chain over it
            foreach (var tableAt in new[] { At(0, 21, 8), At(0, 29, 8) })
            foreach (var st in FindObjectsByType<ItemStand>(FindObjectsSortMode.None).Where(t => Vector3.Distance(t.transform.position, tableAt) < 2f))
            {
                var vis = st.m_attachOther.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToList();
                var bounds = vis.Count > 0 ? vis.Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; }) : new Bounds();
                Plugin.Log.LogInfo($"townsfolk self-test torture: ({(tableAt - yew.Origin).x:0}) {Name(st.GetComponent<ZNetView>().GetZDO().GetInt(ZDOVars.s_item))} lies {bounds.min.y - tableAt.y - 0.817f:0.00}..{bounds.max.y - tableAt.y - 0.817f:0.00} m above the table top, {bounds.size.x:0.00} x {bounds.size.z:0.00} m; stand disc shown {st.GetComponentsInChildren<Renderer>().Any(r => r.enabled && !r.transform.IsChildOf(st.m_attachOther))}");
            }
            var table = At(0, 21, 8);
            var tb = FindObjectsByType<ZNetView>(FindObjectsSortMode.None).Where(v => v.name.StartsWith("piece_table(")).OrderBy(v => Vector3.Distance(v.transform.position, table)).FirstOrDefault();
            if (tb != null)
            {
                var stains = tb.GetComponentsInChildren<MeshRenderer>().Where(r => r.name == "u5_blood").ToList();
                Plugin.Log.LogInfo($"townsfolk self-test torture: blood stains {stains.Count}{(stains.Count > 0 ? $", {stains[0].bounds.center.y - table.y - 0.817f:0.000} m above the top, shader {stains[0].sharedMaterial.shader.name}" : "")}");
            }
            var near = FindObjectsByType<ZNetView>(FindObjectsSortMode.None).Where(v => Vector3.Distance(v.transform.position, table) < 4f && (v.name.StartsWith("CastleKit_decal") || v.name.StartsWith("dvergrprops_hooknchain") || v.name.StartsWith("woodiron_beam")));
            foreach (var v in near)
                Plugin.Log.LogInfo($"townsfolk self-test torture: {v.name} at {v.transform.position - table} scale {v.transform.localScale}, components {string.Join(",", v.GetComponents<Component>().Select(c => c.GetType().Name))}");
        }

        static void BoltHit(Projectile __instance, Collider collider, Vector3 hitPoint)
        {
            if (!__instance.name.StartsWith(Ballistas.ProjectileName)) return;
            var castle = Settlements.Get(17);
            Plugin.Log.LogInfo($"townsfolk self-test bolt hit {(collider != null ? collider.name + " of " + collider.transform.root.name : "nothing")} at {hitPoint - (castle?.Origin ?? Vector3.zero)}");
        }

        /// <summary>One settlement: its pieces, its people (where they stand, whether on their walkable floors), its statues.</summary>
        IEnumerator Town(int number, int rounds = 3)
        {
            var town = Settlements.Get(number);
            if (town == null) { Plugin.Log.LogWarning($"townsfolk self-test town {number}: not built"); yield break; }
            yield return Generate(town.Origin);
            yield return new WaitForSeconds(5f);
            var near = FindObjectsByType<ZNetView>(FindObjectsSortMode.None).Where(v => Vector3.Distance(v.transform.position, town.Origin) < town.Site.Half + 5f).ToList();
            Plugin.Log.LogInfo($"townsfolk self-test town {town.Location.Name}: {near.Count} objects ({near.Count(v => v.name.StartsWith(Structures.PrefabName))} structures); top kinds {string.Join(", ", near.GroupBy(v => Utils.GetPrefabName(v.gameObject)).OrderByDescending(g => g.Count()).Take(12).Select(g => $"{g.Key} {g.Count()}"))}");
            // the wild's things (not the settlement's) reaching into its square
            foreach (var v in FindObjectsByType<ZNetView>(FindObjectsSortMode.None))
            {
                var z = v.GetZDO();
                if (z == null || z.GetBool(Settlements.FixedKey) || v.GetComponent<Character>() != null || v.name.StartsWith(Structures.PrefabName)
                    || v.GetComponent<LootSpot>() != null || v.GetComponent<U5Chest>() != null) continue;
                var cols = v.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger).ToList();
                if (cols.Count == 0) continue;
                var bb = cols[0].bounds; foreach (var c in cols) bb.Encapsulate(c.bounds);
                float h = town.Site.Half;
                if (bb.max.x < town.Origin.x - h || bb.min.x > town.Origin.x + h || bb.max.z < town.Origin.z - h || bb.min.z > town.Origin.z + h) continue;
                Plugin.Log.LogInfo($"townsfolk self-test town: wild {Utils.GetPrefabName(v.gameObject)} at {v.transform.position - town.Origin} reaches {bb.min - town.Origin}..{bb.max - town.Origin} (square ±{h:0})");
            }
            // anything at all with a collider in the square that is not the settlement's own (or the ground, or someone)
            {
                float h = town.Site.Half;
                var seenRoots = new System.Collections.Generic.Dictionary<string, int>();
                foreach (var col in Physics.OverlapBox(town.Origin + Vector3.up * 10f, new Vector3(h, 40f, h)))
                {
                    if (col.GetType().Name == "TerrainCollider" || col.GetComponentInParent<Heightmap>() != null || col.GetComponentInParent<Character>() != null) continue;
                    var nv = col.GetComponentInParent<ZNetView>();
                    if (nv != null && (nv.GetZDO()?.GetBool(Settlements.FixedKey) == true || nv.name.StartsWith(Structures.PrefabName))) continue;
                    var root = nv != null ? nv.gameObject : col.transform.root.gameObject;
                    string key = $"{root.name} (zdo {nv != null}) at {root.transform.position - town.Origin}";
                    seenRoots[key] = seenRoots.TryGetValue(key, out int k) ? k + 1 : 1;
                }
                foreach (var kv in seenRoots) Plugin.Log.LogInfo($"townsfolk self-test town: in the square: {kv.Key} x{kv.Value}");
            }
            // the settlement's own rocks, and how far they reach
            foreach (var v in near.Where(v => v.name.StartsWith("Rock") || v.name.StartsWith("rock") || v.name.StartsWith("stone_pile")))
            {
                var cols = v.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger).ToList();
                if (cols.Count == 0) { Plugin.Log.LogInfo($"townsfolk self-test town: rock {v.name} at {v.transform.position - town.Origin}, no colliders"); continue; }
                var bb = cols[0].bounds; foreach (var c in cols) bb.Encapsulate(c.bounds);
                Plugin.Log.LogInfo($"townsfolk self-test town: rock {Utils.GetPrefabName(v.gameObject)} at {v.transform.position - town.Origin}, {bb.size.x:0.0} x {bb.size.y:0.0} x {bb.size.z:0.0} m");
            }
            // the ground poking up through the ground floor's walked tiles (four points round each tile's middle)
            {
                var walk0 = town.Plan.Walk.TryGetValue(0, out var w0) ? w0 : null;
                int bumps = 0;
                for (int c = 0; c < 32 * 32 && walk0 != null; c++)
                {
                    if (!walk0[c]) continue;
                    var (tx, ty, tz) = town.Plan.TilePos(0, c);
                    float worst = float.MinValue;
                    foreach (var (dx, dz) in new[] { (0f, 0f), (0.8f, 0.8f), (-0.8f, 0.8f), (0.8f, -0.8f), (-0.8f, -0.8f) })
                        worst = Mathf.Max(worst, ZoneSystem.instance.GetGroundHeight(town.Origin + new Vector3(tx + dx, 0, tz + dz)) - (town.Origin.y + ty));
                    if (worst > 0.3f) { bumps++; if (bumps <= 12) Plugin.Log.LogInfo($"townsfolk self-test town: ground {worst:0.0} m above the floor at tile ({c % 32},{c / 32})"); }
                }
                Plugin.Log.LogInfo($"townsfolk self-test town {town.Location.Name}: {bumps} floor tiles with the ground above them");
            }
            foreach (var t in FindObjectsByType<CreatureTalker>(FindObjectsSortMode.None).Where(t => Vector3.Distance(t.transform.position, town.Origin) < town.Site.Half + 5f))
            {
                var c = t.GetComponent<Character>();
                var session = t.Script != null ? new Data.TalkSession(t.Script, Plugin.U5.Ovl, new PlayerTalkWorld(t)) : null;
                Plugin.Log.LogInfo($"townsfolk self-test town: talking creature {t.GetHoverName()} ({Utils.GetPrefabName(t.gameObject)}) at {t.transform.position - town.Origin}, faction {c.GetFaction()}, AI on {t.GetComponents<BaseAI>().Any(a => a.enabled)}, hp {c.GetHealth():0}, hover \"{c.GetHoverText().Replace("\n", " ")}\", says \"{session?.TakeOutput().Replace("\n", " ").Trim()}\"");
            }
            var ports = FindObjectsByType<Portcullis>(FindObjectsSortMode.None).Where(p => Vector3.Distance(p.transform.position, town.Origin) < town.Site.Half + 5f).ToList();
            if (ports.Count > 0)
                Plugin.Log.LogInfo($"townsfolk self-test town: {ports.Count} portcullis grates, night {EnvMan.IsNight()} (hour {TownNpc.Hour()}); {string.Join("; ", ports.Take(4).Select(p => $"at {p.transform.position - town.Origin}, down {p.IsDown}, drawn {p.GetComponentsInChildren<Renderer>().Any(r => r.enabled)}, solid {p.GetComponentsInChildren<Collider>().Any(c => c.enabled)}, hover \"{p.GetComponent<Door>()?.GetHoverText()}\""))}");
            if (ports.Count > 0 && System.Environment.GetEnvironmentVariable("U5_SELFTEST_NOON") == "1")
            {
                // noon: up and gone
                var env = EnvMan.instance;
                env.m_debugTimeOfDay = true;
                env.m_debugTime = 0.5f;
                yield return new WaitForSeconds(6f);
                Plugin.Log.LogInfo($"townsfolk self-test town: at noon (night {EnvMan.IsNight()}): {string.Join("; ", ports.Select(p => $"at {p.transform.position - town.Origin}, down {p.IsDown}, drawn {p.GetComponentsInChildren<Renderer>().Any(r => r.enabled)}, solid {p.GetComponentsInChildren<Collider>().Any(c => c.enabled)}, bodies {string.Join(",", p.GetComponentsInChildren<Rigidbody>(true).Select(r => $"{r.name} kin {r.isKinematic} at {(r.position - town.Origin).y:0.0}"))}"))}");
                env.m_debugTimeOfDay = false;
            }
            // floors a ladder passes through: the floor piece, its material faded
            var clear = near.Where(v => v.GetZDO()?.GetString(Settlements.LookKey) == "see_through").ToList();
            if (clear.Count > 0)
                Plugin.Log.LogInfo($"townsfolk self-test town: {clear.Count} see-through floors ({string.Join(", ", clear.GroupBy(v => Utils.GetPrefabName(v.gameObject)).Select(g => $"{g.Key} {g.Count()}"))}); e.g. {clear[0].name} at {clear[0].transform.position - town.Origin}, materials {string.Join(", ", clear[0].GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => $"{m.name} queue {m.renderQueue}").Distinct().Take(3))}");
            foreach (var f in near.Where(v => v.name.StartsWith("fermenter")).Take(6))
            {
                var cols = f.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger).ToList();
                if (cols.Count == 0) continue;
                var bb = cols[0].bounds; foreach (var c in cols) bb.Encapsulate(c.bounds);
                var l = f.transform.position - town.Origin;
                int fc = town.Plan.CellAt(l.x, l.z);
                Plugin.Log.LogInfo($"townsfolk self-test town: barrel at tile ({fc % 32},{fc / 32}) tilted {Vector3.Angle(f.transform.up, Vector3.up):0}°, solid {bb.min.y - town.Origin.y:0.00}..{bb.max.y - town.Origin.y:0.00} m, {bb.size.x:0.0} x {bb.size.z:0.0}");
            }
            var magic = near.Where(v => v.GetZDO()?.GetString(Settlements.LookKey) == "magic_lock").ToList();
            if (magic.Count > 0)
                Plugin.Log.LogInfo($"townsfolk self-test town: {magic.Count} magically locked doors ({string.Join(", ", magic.Select(v => $"{Utils.GetPrefabName(v.gameObject)} at {v.transform.position - town.Origin}, lock {v.GetZDO().GetString(Settlements.LockKey)}, light {v.GetComponentInChildren<Light>() != null}"))})");
            foreach (var th in near.Where(v => v.name.StartsWith("piece_throne")))
                Plugin.Log.LogInfo($"townsfolk self-test town: throne at {th.transform.position - town.Origin} facing {th.transform.forward}");
            var back = Respawn.Point();
            if (back.HasValue && Vector3.Distance(back.Value, town.Origin) < 60f)
            {
                bool floor = Physics.Raycast(back.Value + Vector3.up, Vector3.down, out var fh, 3f, LayerMask.GetMask("piece"));
                Plugin.Log.LogInfo($"townsfolk self-test town: the dead come back at {back.Value - town.Origin}, standing on {(floor ? $"{fh.collider.transform.root.name} at {fh.point.y - town.Origin.y:0.00}" : "nothing")}");
            }
            foreach (var st in near.Where(v => v.name.StartsWith("StatueEvil")))
                Plugin.Log.LogInfo($"townsfolk self-test town: gargoyle at {st.transform.position - town.Origin}, facing {st.transform.forward}");
            // loot: the settlement's chests and the items lying or hidden in it
            foreach (var ch in near.Where(v => v.GetComponent<U5Chest>() != null))
            {
                var inv = ch.GetComponent<Container>().GetInventory();
                Plugin.Log.LogInfo($"townsfolk self-test town: chest at {ch.transform.position - town.Origin}, quality {ch.GetZDO().GetInt(Treasure.QualityKey)}, holds {string.Join(", ", inv.GetAllItems().Select(i => $"{i.m_stack} {i.m_shared.m_name}"))}");
            }
            foreach (var spot in near.Where(v => v.GetComponent<LootSpot>() != null))
            {
                var z = spot.GetZDO();
                var ls = spot.GetComponent<LootSpot>();
                bool onFloor = Physics.Raycast(spot.transform.position + Vector3.up * 0.5f, Vector3.down, out var lh, 2f, LayerMask.GetMask("piece", "static_solid", "Default", "terrain"));
                Plugin.Log.LogInfo($"townsfolk self-test town: loot {(z.GetBool(Treasure.HiddenKey) ? "hidden" : "lying")} at {spot.transform.position - town.Origin}: {(U5InValheim.Data.LootKind)z.GetInt(Treasure.KindKey)} {z.GetInt(Treasure.ValueKey)} x{z.GetInt(Treasure.CountKey)} (index {z.GetInt(Treasure.IndexKey, -1)}), hover \"{ls.GetHoverText().Replace("\n", " ")}\", on {(onFloor ? $"{lh.collider.transform.root.name} {lh.distance - 0.5f:0.00} m below" : "nothing")}, drawn {spot.GetComponentsInChildren<Renderer>().Length} renderers");
            }
            // dying monsters leave chests by their treasure (a troll's 15: about half of them)
            {
                var at = town.Origin + new Vector3(0, 0, -town.Site.Half - 20f);
                at.y = ZoneSystem.instance.GetGroundHeight(at) + 0.5f;
                int before = Object.FindObjectsOfType<U5Chest>().Length, killed = 0;
                for (int i = 0; i < 12; i++)
                {
                    var troll = UltimaCreatures.Spawn(41, at + new Vector3(i * 3f, 0, 0), false);
                    if (troll == null) continue;
                    troll.Damage(new HitData { m_damage = new HitData.DamageTypes { m_damage = 1e6f }, m_point = troll.transform.position });
                    killed++;
                }
                yield return new WaitForSeconds(3f);
                var chests = Object.FindObjectsOfType<U5Chest>().Where(c => Vector3.Distance(c.transform.position, at) < 60f).ToList();
                Plugin.Log.LogInfo($"townsfolk self-test town: {killed} trolls killed, {Object.FindObjectsOfType<U5Chest>().Length - before} chests left ({string.Join("; ", chests.Take(3).Select(c => $"trapped {c.GetComponent<ZNetView>().GetZDO().GetBool(Treasure.TrapKey)}: {string.Join(", ", c.GetComponent<Container>().GetInventory().GetAllItems().Select(it => $"{it.m_stack} {it.m_shared.m_name}"))}"))}), Valheim drops near: {Object.FindObjectsOfType<ItemDrop>().Count(d => Vector3.Distance(d.transform.position, at) < 60f)}");
            }
            for (int round = 0; round < rounds; round++)
            {
                yield return new WaitForSeconds(20f);
                var folk = FindObjectsByType<TownNpc>(FindObjectsSortMode.None).Where(n => n.Location == number).ToList();
                int off = 0;
                foreach (var n in folk)
                {
                    var l = n.transform.position - town.Origin;
                    int c = town.Plan.CellAt(l.x, l.z);
                    bool walk = town.Plan.Walk.TryGetValue(n.Floor, out var w) && c >= 0 && w[c];
                    float floorY = town.Plan.LevelY(n.Floor);
                    bool level = Mathf.Abs(l.y - floorY) < 1.2f || n.Floor == 0;
                    if (!walk || !level) { off++; Plugin.Log.LogInfo($"townsfolk self-test town: {n.GetHoverName()} floor {n.Floor} at {l} (tile {c % 32},{c / 32}, walkable {walk}, {l.y - floorY:0.0} m off its floor)"); }
                }
                Plugin.Log.LogInfo($"townsfolk self-test town {town.Location.Name} t={(round + 1) * 20}s: {folk.Count} people, {off} off their floors; hour {TownNpc.Hour()}; alarm {town.Watch.Alarmed}");
                foreach (var r in FindObjectsByType<ResidentMonster>(FindObjectsSortMode.None).Where(r => Vector3.Distance(r.transform.position, town.Origin) < town.Site.Half + 10f))
                {
                    var c = r.GetComponent<Character>();
                    var m = r.GetComponent<MonsterAI>();
                    var target = m != null ? m.GetTargetCreature() : null;
                    Plugin.Log.LogInfo($"townsfolk self-test town: resident {c.m_name} ({Utils.GetPrefabName(r.gameObject)}) AI {r.GetComponent<ZNetView>().GetZDO().GetInt(ResidentMonster.AiKey)}, its AI on {m != null && m.enabled}, sees {(m != null ? m.m_viewRange : 0):0} m, at {r.transform.position - town.Origin}, hp {c.GetHealth():0}, target {(target != null ? target.GetHoverName() : "none")}");
                }
            }
        }

        /// <summary>An inn's room (Britain's Wayfarer Inn) and a camp out in the wild, with a stand-in player.</summary>
        IEnumerator Sleeping()
        {
            // (U5_SLEEP_TOWN: another inn's settlement than Britain)
            var britain = Settlements.Get(int.TryParse(System.Environment.GetEnvironmentVariable("U5_SLEEP_TOWN"), out int innTown) ? innTown : 2);
            yield return Generate(britain.Origin);
            yield return new WaitForSeconds(3f);
            var inn = Inns.InnOf(britain);
            var bedAt = inn != null ? Inns.InnBedAt(britain, inn) : null;
            var body = Instantiate(ZNetScene.instance.GetPrefab("Player"), britain.Origin + new Vector3(0, 0.3f, -6f), Quaternion.identity).GetComponent<Player>();
            body.enabled = false;
            var beds = FindObjectsByType<Bed>(FindObjectsSortMode.None).Where(b => Vector3.Distance(b.transform.position, britain.Origin) < 60f).ToList();
            bool Rests(Bed b) => Inns.MayRest(body, b);
            Plugin.Log.LogInfo($"townsfolk self-test sleep: {inn?.Name} rest {(inn != null ? Plugin.U5.Shops.InnPrice(inn, 15) : -1)} gp, its bed at {(bedAt.HasValue ? (bedAt.Value - britain.Origin).ToString() : "none")}; {beds.Count} beds in Britain, before paying {beds.Count(Rests)} open");
            Inns.Rent(body, inn);
            var open = beds.Where(Rests).ToList();
            Plugin.Log.LogInfo($"townsfolk self-test sleep: after paying {open.Count} beds open: {string.Join(", ", open.Select(b => $"{b.transform.position - britain.Origin}"))}; record {body.m_customData["u5_inn"]}, now {ZNet.instance.GetTimeSeconds():0}");
            // as the player does it: E on each of the inn's beds (the town bed patch, then lying down)
            Player.m_localPlayer = body;
            try
            {
                foreach (var b in beds.OrderBy(b => Vector3.Distance(b.transform.position, bedAt ?? britain.Origin)).Take(5))
                {
                    if (body.IsAttached()) body.AttachStop();
                    bool used = b.Interact(body, false, false);
                    Plugin.Log.LogInfo($"townsfolk self-test sleep: E on the bed at {b.transform.position - britain.Origin} ({b.name}): result {used}, in bed {body.InBed()}, attached {body.IsAttached()}");
                }
            }
            finally { Player.m_localPlayer = null; }
            // a camp: out in the wild, then again (moved), then in the town (refused)
            // (dry land out of town: the first of a ring of points round Britain 2 m above the sea; Height is from sea level)
            var shape = Britannia.Active;
            var wild = Enumerable.Range(0, 24).Select(i => britain.Origin + Quaternion.Euler(0, i * 15, 0) * Vector3.forward * 150f)
                .FirstOrDefault(v => shape.Height(v.x, v.z) > 2f);
            yield return Generate(wild);
            yield return new WaitForSeconds(2f);
            Player.m_localPlayer = body;
            try
            {
                foreach (var (where, label) in new[] { (wild, "wild"), (wild + new Vector3(12, 0, 0), "moved"), (britain.Origin + new Vector3(0, 0, -6f), "in town") })
                {
                    var p = where; p.y = ZoneSystem.instance.GetGroundHeight(p) + 0.1f;
                    body.transform.position = p;
                    body.GetComponent<Rigidbody>().position = p;
                    Camp.Pitch(body);
                    yield return new WaitForSeconds(0.5f);
                    var leans = FindObjectsByType<LeanTo>(FindObjectsSortMode.None);
                    var fires = FindObjectsByType<Fireplace>(FindObjectsSortMode.None).Where(f => f.GetComponent<ZNetView>()?.GetZDO()?.GetBool(Settlements.FixedKey) == true && f.name.StartsWith(Camp.FirePrefab)).ToList();
                    Plugin.Log.LogInfo($"townsfolk self-test sleep: camp {label}: {leans.Length} lean-to(s) {string.Join(", ", leans.Select(l => $"{l.transform.position - p} hover \"{l.GetHoverText().Replace("\n", " ")}\""))}; {fires.Count} camp fire(s) {string.Join(", ", fires.Select(f => $"{f.transform.position - p} infinite {f.m_infiniteFuel}"))}; near {Camp.Near(p, 6f)}");
                    yield return new WaitForSeconds(1f);
                }
            }
            finally { Player.m_localPlayer = null; }
            // one camp each: Fred pitches, Mary pitches, Mary pitches again (her first goes, Fred's stays)
            foreach (var l in FindObjectsByType<LeanTo>(FindObjectsSortMode.None)) ZNetScene.instance.Destroy(l.gameObject);
            yield return new WaitForSeconds(0.5f);
            var fred = body;
            var mary = Instantiate(ZNetScene.instance.GetPrefab("Player"), wild, Quaternion.identity).GetComponent<Player>();
            mary.enabled = false;
            fred.m_customData.Remove(Camp.CampKey);
            foreach (var (who, dx, label) in new[] { (fred, 0f, "Fred"), (mary, 20f, "Mary"), (mary, 40f, "Mary again") })
            {
                var p = wild + new Vector3(dx, 0, 0); p.y = ZoneSystem.instance.GetGroundHeight(p) + 0.1f;
                who.transform.position = p;
                who.GetComponent<Rigidbody>().position = p;
                Player.m_localPlayer = who;
                Camp.Pitch(who);
                Player.m_localPlayer = null;
                yield return new WaitForSeconds(0.5f);
                Plugin.Log.LogInfo($"townsfolk self-test sleep: {label} pitches: lean-tos at {string.Join(", ", FindObjectsByType<LeanTo>(FindObjectsSortMode.None).Select(l => $"{(l.transform.position - wild).x:0}"))} (x from the first)");
            }
            ZNetScene.instance.Destroy(mary.gameObject);
            ZNetScene.instance.Destroy(body.gameObject);
            // an ambush comes close (as Sleep's, 10-18 m from the camp)
            string what = null;
            for (int i = 0; i < 6 && what == null; i++) what = UltimaSpawner.Encounter(wild, "the camp", new System.Random(i), 10f, 18f);
            Plugin.Log.LogInfo($"townsfolk self-test sleep: ambush: {what ?? "nothing could come"}");
        }

        static string Name(int hash) => hash == 0 ? "-" : ObjectDB.instance?.GetItemPrefab(hash)?.name ?? ZNetScene.instance?.GetPrefab(hash)?.name ?? hash.ToString();
    }
}
