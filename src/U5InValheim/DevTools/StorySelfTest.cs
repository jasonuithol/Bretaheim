using System;
using System.Collections;
using System.Linq;
using U5InValheim.Data;
using U5InValheim.WorldGen;
using UnityEngine;

namespace U5InValheim.DevTools
{
    /// <summary>
    /// U5_SELFTEST=story on a dedicated server (run by TownsfolkSelfTest): Blackthorn's palace
    /// built, the dungeon cell the prisoner is left in (what stands under it), Blackthorn there;
    /// the interrogation and the ending played through offline with the server's files; Lord
    /// British freed (the world key) and Blackthorn gone into exile, then the key taken back.
    /// </summary>
    public static class StorySelfTest
    {
        static void Log(string s) => Plugin.Log.LogInfo("story self-test " + s);

        public static IEnumerator Run(Func<Vector3, IEnumerator> generate)
        {
            var palace = Settlements.Get(BlackthornCapture.Palace);
            if (palace == null) { Log("Blackthorn's palace is not built"); yield break; }
            yield return generate(palace.Origin);
            var cell = BlackthornCapture.Cell().Value;
            bool floor = Physics.Raycast(cell + Vector3.up * 1f, Vector3.down, out var hit, 3f, ~0, QueryTriggerInteraction.Ignore);
            bool roof = Physics.Raycast(cell + Vector3.up * 0.5f, Vector3.up, out var above, 6f, ~0, QueryTriggerInteraction.Ignore);
            Log($"cell (10,7,-1) at {cell - palace.Origin} from the palace's origin: floor {(floor ? $"{hit.collider.name} {cell.y - hit.point.y:0.00} m below" : "none")}, ceiling {(roof ? $"{above.collider.name} {above.distance + 0.5f:0.0} m up" : "none")}");
            var blackthorn = Character.GetAllCharacters().Select(TownsfolkPatches.NpcOf).Where(n => n != null && n.Location == BlackthornCapture.Palace).ToList();
            Log($"palace people: {blackthorn.Count}, guards {blackthorn.Count(n => n.IsGuard)}, Blackthorn {blackthorn.Count(n => n.CreatureType == 14)}");

            // the scenes, played offline with this server's files
            var ovl = Plugin.U5.Ovl;
            var bt = new BlackthornScene(ovl, ScenePlayer.Read("MISCMSG.DAT"), ScenePlayer.Read("MISCMAPS.DAT"), "Tester", true, 2, "Iolo", Enumerable.Range(0, 8).Select(Shrines.IsRuined).ToArray());
            int asked = 0, beats = 0;
            foreach (var b in bt.Run()) { beats++; if (b.Kind == BeatKind.Ask) bt.Answer = asked++ == 0 ? "no" : bt.Mantra(bt.Virtue); }
            Log($"interrogation: {beats} beats, asked {asked} times about {(bt.Virtue >= 0 ? bt.VirtueName(bt.Virtue) : "nothing")}, betrayed {bt.Betrayed}, companion killed {bt.CompanionKilled}; screen drawn {bt.View.Render(Plugin.U5.Tiles, ScenePlayer.Font).Pixels.Count(p => p != 0)} px");
            var eg = new EndgameScene(ovl, ScenePlayer.Read("MISCMAPS.DAT"), ScenePlayer.Read("ENDMSG.DAT"), new[] { "Tester" }, true, new System.Random(1));
            var now = Calendar.Now;
            int pictures = 0;
            eg.Finale = () => EndStory.Finale(Plugin.U5.Directory, ovl, "Tester", now.Year, now.Month, now.Day);
            foreach (var b in eg.Run()) { if (b.Kind == BeatKind.YesNo) eg.Yes = true; if (b.Kind == BeatKind.Picture) pictures++; if (b.Endless) break; }
            Log($"ending: won {eg.Won}, {pictures} pictures; Doom's mirror {(Endgame.DoomMirror?.Invoke()?.ToString() ?? "not built")}");

            // Lord British freed: Blackthorn leaves his palace
            WorldKeys.Set(Endgame.FreedKey, now.DateText);
            yield return new WaitForSeconds(7f);
            int left = Character.GetAllCharacters().Select(TownsfolkPatches.NpcOf).Count(n => n != null && n.Location == BlackthornCapture.Palace && n.CreatureType == 14);
            Log($"freed on {Endgame.FreedOn}: Blackthorn {(left == 0 ? "gone into exile" : "still there")}");
            ZoneSystem.instance.RemoveGlobalKey(Endgame.FreedKey);
            yield return new WaitForSeconds(1f);
            Log($"key taken back: freed {Endgame.Freed}");
        }
    }
}
