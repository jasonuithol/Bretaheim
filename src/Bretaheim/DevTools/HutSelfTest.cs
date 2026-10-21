using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Bretaheim.Data;
using Bretaheim.WorldGen;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Bretaheim.DevTools
{
    /// <summary>
    /// U5_SELFTEST=hut (a dedicated server): Iolo's hut built, its tool chest and workbench; a
    /// stand-in player body unmade in the hut brings Hildir and locks the doors, made sends her
    /// away; a player's axe on a tree and the recipes they are offered; the introduction's and the
    /// gypsy's files read. Logged as "hut self-test …".
    /// </summary>
    public static class HutSelfTest
    {
        static void Log(string s) => Plugin.Log.LogInfo("hut self-test " + s);

        public static IEnumerator Run(Func<Vector3, IEnumerator> generate)
        {
            var hut = IolosHut.Hut;
            if (hut == null) { Log("FAIL: Iolo's hut is not built"); yield break; }
            var spawn = hut.Origin + new Vector3(hut.Plan.SpawnX, 0.3f, hut.Plan.SpawnZ);
            Log($"hut at {hut.Origin}, spawn {spawn}, Hildir's spot {IolosHut.HildirSpot(hut)}");
            yield return generate(hut.Origin);

            // the tool chest and the workbench
            var chests = Object.FindObjectsByType<Container>(FindObjectsSortMode.None).Where(c => IolosHut.InHut(c.transform.position)).ToList();
            Log("chests: " + string.Join("; ", chests.Select(c => $"{c.name} [{string.Join(",", c.GetInventory().GetAllItems().Select(i => i.m_dropPrefab?.name))}]")));
            var benches = Object.FindObjectsByType<CraftingStation>(FindObjectsSortMode.None).Where(c => IolosHut.InHut(c.transform.position)).ToList();
            Log($"crafting stations: {benches.Count} ({string.Join(",", benches.Select(b => b.name))})");

            // an unmade newcomer: Hildir comes, the doors are locked
            var body = Object.Instantiate(ZNetScene.instance.GetPrefab("Player"), spawn, Quaternion.identity).GetComponent<Player>();
            body.enabled = false;
            var zdo = body.GetComponent<ZNetView>().GetZDO();
            zdo.Set(IolosHut.MadeKey, 1);
            IolosHut.StandIns.Add(zdo);
            for (int i = 0; i < 6; i++) yield return new WaitForSeconds(1f);
            var hildir = Object.FindObjectsByType<IolosHut.Gypsy>(FindObjectsSortMode.None).ToList();
            Log($"unmade: Hildir {hildir.Count} ({string.Join(",", hildir.Select(h => $"{h.GetHoverName()} {Vector3.Distance(h.transform.position, spawn):0.0} m from the spawn"))}), locked {IolosHut.Locked}, players {Player.GetAllPlayers().Count}");
            var doors = Object.FindObjectsByType<Door>(FindObjectsSortMode.None).Where(d => IolosHut.InHut(d.transform.position)).ToList();
            foreach (var d in doors.Take(3))
            {
                bool used = d.Interact(body, false, false);
                yield return new WaitForSeconds(0.5f);
                Log($"door {d.name} at {d.transform.position - hut.Origin}: interact {used}, state {d.GetComponent<ZNetView>().GetZDO().GetInt(ZDOVars.s_state)}");
            }

            // a tree: the player's axe does nothing
            var tree = Object.FindObjectsByType<TreeBase>(FindObjectsSortMode.None).OrderBy(t => Vector3.Distance(t.transform.position, spawn)).FirstOrDefault();
            if (tree != null)
            {
                float before = tree.GetComponent<ZNetView>().GetZDO().GetFloat(ZDOVars.s_health, tree.m_health);
                var hit = new HitData { m_point = tree.transform.position, m_toolTier = 10 };
                hit.m_damage.m_chop = 500;
                hit.SetAttacker(body);
                tree.Damage(hit);
                yield return new WaitForSeconds(1f);
                bool gone = tree == null;
                Log($"tree {(gone ? "felled (FAIL)" : tree.name)}: health {before:0} -> {(gone ? 0 : tree.GetComponent<ZNetView>().GetZDO().GetFloat(ZDOVars.s_health, tree.m_health)):0}");
            }
            else Log("no tree near the hut");

            // the recipes a player is offered
            var recipes = new List<Recipe>();
            body.GetAvailableRecipes(ref recipes);
            Log($"recipes offered: {recipes.Count}");

            // made: Hildir goes, the doors open to the hand
            zdo.Set(IolosHut.MadeKey, 2);
            for (int i = 0; i < 4; i++) yield return new WaitForSeconds(1f);
            Log($"made: Hildir {Object.FindObjectsByType<IolosHut.Gypsy>(FindObjectsSortMode.None).Length}, locked {IolosHut.Locked}");
            foreach (var d in doors.Take(1))
            {
                bool used = d.Interact(body, false, false);
                yield return new WaitForSeconds(0.5f);
                Log($"door {d.name}: interact {used}, state {d.GetComponent<ZNetView>().GetZDO().GetInt(ZDOVars.s_state)}");
            }
            IolosHut.StandIns.Remove(zdo);
            ZNetScene.instance.Destroy(body.gameObject);

            // the introduction's and the gypsy's files
            var story = Story.Available(Plugin.U5.Directory) ? Story.Load(Plugin.U5.Directory, Plugin.U5.Ovl) : null;
            Log($"intro: {(story == null ? "files missing" : $"{story.Count} screens")}; gypsy files {VirtueCasting.Available(Plugin.U5.Directory)}");

            // the shrines: Honesty's and the Codex built, their kneeling stones; a ruin and its restoration
            var sd = Shrines.Data;
            foreach (var (x, y, name) in new[] { (sd.X[0], sd.Y[0], "Honesty"), (ShrineData.CodexX, ShrineData.CodexY, "the Codex") })
            {
                var at = Britannia.TileCentre(x, y);
                yield return generate(at);
                var altars = Object.FindObjectsByType<ShrineAltar>(FindObjectsSortMode.None).Where(a => Vector3.Distance(a.transform.position, at) < 20f).ToList();
                int pieces = Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None).Count(v => v.GetZDO() != null && v.GetZDO().GetBool(Settlements.FixedKey) && Vector3.Distance(v.transform.position, at) < 16f);
                Log($"shrine of {name} at {at}: {pieces} pieces, kneeling stones {altars.Count} ({string.Join(",", altars.Select(a => a.GetHoverName() + " | " + a.GetHoverText().Replace("\n", " | ")))}), sign {Object.FindObjectsByType<Sign>(FindObjectsSortMode.None).Count(s => Vector3.Distance(s.transform.position, at) < 16f)}");
            }
            Shrines.Desecrate(2);
            yield return new WaitForSeconds(1f);
            bool ruined = Shrines.IsRuined(2);
            Shrines.Restore(2);
            yield return new WaitForSeconds(1f);
            Log($"shrine of Valour ruined {ruined}, restored {!Shrines.IsRuined(2)}");
        }
    }
}
