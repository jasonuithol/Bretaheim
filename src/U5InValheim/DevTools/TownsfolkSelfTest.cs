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
            yield return null;
            foreach (var zdo in zdos) ZDOMan.instance.DestroyZDO(zdo);
            Plugin.Log.LogInfo($"townsfolk self-test: removed {zdos.Count} again");
        }

        static string Name(int hash) => hash == 0 ? "-" : ObjectDB.instance?.GetItemPrefab(hash)?.name ?? ZNetScene.instance?.GetPrefab(hash)?.name ?? hash.ToString();
    }
}
