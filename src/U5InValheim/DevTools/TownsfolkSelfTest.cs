using System.Collections;
using System.Linq;
using U5InValheim.WorldGen;
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
            while (ZNet.instance == null || !ZNet.instance.IsDedicated() || ZNetScene.instance == null || Settlements.Get(2) == null || EnvMan.instance == null)
                yield return new WaitForSeconds(1f);
            yield return new WaitForSeconds(5f);
            var b = Settlements.Get(2);
            TownsfolkSpawner.Spawn(b, ZoneSystem.SpawnMode.Full, null);
            var folk = FindObjectsByType<TownNpc>(FindObjectsSortMode.None).OrderBy(n => n.GetHoverName()).ToList();
            var zdos = folk.Select(n => n.GetComponent<ZNetView>().GetZDO()).ToList();
            Plugin.Log.LogInfo($"townsfolk self-test hour {TownNpc.Hour()}: {folk.Count} people: " +
                string.Join("; ", folk.Select(n => { var p = n.transform.position - b.Origin; return $"{n.GetHoverName()} ({p.x:0.0},{p.y:0.0},{p.z:0.0})"; })));
            foreach (var n in folk.Where(f => f.GetHoverName() == "Eb" || f.GetHoverName() == "Guard" || f.GetHoverName() == "Annon").Take(3))
            {
                var zdo = n.GetComponent<ZNetView>().GetZDO();
                Plugin.Log.LogInfo($"  {n.GetHoverName()}: model {zdo.GetInt(ZDOVars.s_modelIndex)} chest {Name(zdo.GetInt(ZDOVars.s_chestItem))} legs {Name(zdo.GetInt(ZDOVars.s_legItem))} helmet {Name(zdo.GetInt(ZDOVars.s_helmetItem))} right {Name(zdo.GetInt(ZDOVars.s_rightItem))} left {Name(zdo.GetInt(ZDOVars.s_leftItem))} hair {Name(zdo.GetInt(ZDOVars.s_hairItem))} beard {Name(zdo.GetInt(ZDOVars.s_beardItem))} scale {n.transform.localScale.x:0.00}" +
                    $" layer {LayerMask.LayerToName(n.gameObject.layer)} components {string.Join(",", n.GetComponents<Component>().Select(c => c.GetType().Name))}");
            }
            // a guard and a greyling: the guard should run at it and strike
            var guard = folk.FirstOrDefault(f => f.GetHoverName() == "Guard" && f.transform.position.y - b.Origin.y < 1f);
            var greyling = ZNetScene.instance.GetPrefab("Greyling");
            if (guard != null && greyling != null)
            {
                // a headless server unloads everything with no player near: not during the fight
                holding = true;
                // on open street three or four tiles from the guard
                var gl = guard.transform.position - b.Origin;
                int gc = b.Plan.CellAt(gl.x, gl.z);
                int spot = Enumerable.Range(0, 1024).Where(c => b.Plan.Walk[0][c] && Mathf.Max(Mathf.Abs(c % 32 - gc % 32), Mathf.Abs(c / 32 - gc / 32)) is int d && d >= 3 && d <= 4)
                    .OrderBy(c => Mathf.Abs(c % 32 - gc % 32) + Mathf.Abs(c / 32 - gc / 32)).FirstOrDefault();
                var (sx, _, sz) = b.Plan.TilePos(0, spot);
                var g = Instantiate(greyling, b.Origin + new Vector3(sx, 0.2f, sz), Quaternion.identity).GetComponent<Character>();
                zdos.Add(g.GetComponent<ZNetView>().GetZDO());
                // no ground is generated here with no player near: keep it from falling through the world
                var body = g.GetComponent<Rigidbody>();
                if (body != null) { body.isKinematic = true; body.useGravity = false; }
                Plugin.Log.LogInfo($"townsfolk self-test fight: guard's swing animation '{HarmonyLib.Traverse.Create(guard).Method("SwingAnimation").GetValue<string>()}'");
                float hp0 = g.GetHealth();
                var guardZdo = guard.GetComponent<ZNetView>().GetZDO();
                float t0 = Time.time;
                g.m_onDeath += () => Plugin.Log.LogInfo($"townsfolk self-test fight: the greyling died at {Time.time - t0:0.0}s after {guardZdo.GetInt(TownsfolkSpawner.SwingKey)} swing(s)");
                var start = guard.transform.position;
                for (int i = 0; i < 12 && g != null && !g.IsDead(); i++)
                {
                    yield return new WaitForSeconds(0.5f);
                    if (g == null || guard == null) { Plugin.Log.LogWarning($"townsfolk self-test fight: gone (guard {guard != null}, greyling {g != null})"); break; }
                    Plugin.Log.LogInfo($"townsfolk self-test fight t={i * 0.5f + 0.5f:0.0}s: guard moved {Vector3.Distance(start, guard.transform.position):0.0} m, greyling {g.GetHealth():0}/{hp0:0} hp, swings {guard.GetComponent<ZNetView>().GetZDO().GetInt(TownsfolkSpawner.SwingKey)}, foe {HarmonyLib.Traverse.Create(guard).Field("foe").GetValue<Character>()?.name}, distance {Vector3.Distance(g.transform.position, guard.transform.position):0.0}, greyling at {g.transform.position - b.Origin}, owner {guard.GetComponent<ZNetView>().IsOwner()}");
                }
            }
            else Plugin.Log.LogWarning($"townsfolk self-test fight: guard {guard != null}, greyling {greyling != null}");
            holding = false;
            yield return null;
            foreach (var zdo in zdos) if (zdo != null && zdo.IsValid()) ZDOMan.instance.DestroyZDO(zdo);
            Plugin.Log.LogInfo($"townsfolk self-test: removed {zdos.Count} again");
        }

        static string Name(int hash) => hash == 0 ? "-" : ObjectDB.instance?.GetItemPrefab(hash)?.name ?? ZNetScene.instance?.GetPrefab(hash)?.name ?? hash.ToString();
    }
}
