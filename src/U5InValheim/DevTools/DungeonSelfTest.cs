using System;
using System.Collections;
using System.Linq;
using U5InValheim.Data;
using U5InValheim.WorldGen;
using U5InValheim.WorldGen.Core;
using UnityEngine;

namespace U5InValheim.DevTools
{
    /// <summary>
    /// U5_SELFTEST=dungeon[:&lt;0-7&gt;] on a dedicated server (default Destard): the entrance is
    /// made with its zone, its word of power opens it, every level is built and stood on, the
    /// hallway chests are put out, a room fills with its monsters (who must stand on the floor,
    /// not fall through it) and is cleared when they die, a trigger changes its room, a secret
    /// door is found. Each check logs "dungeon self-test: …".
    /// </summary>
    public static class DungeonSelfTest
    {
        static void Log(string s) => Plugin.Log.LogInfo("dungeon self-test: " + s);

        public static IEnumerator Run(int d, Func<Vector3, IEnumerator> generate)
        {
            var c = Dungeons.Centre(d);
            if (c == null) { Log($"{d} has no entrance on the surface"); yield break; }
            var at = new Vector3(c.Value.x, Britannia.SeaLevel + Britannia.ShapeAt(c.Value.x, c.Value.y).Height(c.Value.x, c.Value.y), c.Value.y);
            yield return generate(at);
            var e = DungeonEntrance.Get(d);
            Log(e == null ? $"FAIL no entrance of {Dungeons.Name(d)} near {at}" : $"entrance of {Dungeons.Name(d)} at {e.transform.position} facing {e.transform.eulerAngles.y:0}, open {e.Open}");
            if (e == null) yield break;

            // the word of power
            var word = Plugin.U5.Ovl.WordsOfPower[d];
            Log($"word '{word}' is dungeon {DungeonInfo.WordIn(Plugin.U5.Ovl, "we yell " + word.ToLower() + "!")}");
            if (!e.Open) e.SetOpen(true);
            Log($"after the word: open {e.Open}");

            // every level built and its floor where it should be
            for (int level = 0; level < DungeonInfo.Levels; level++)
            {
                var plan = DungeonRunner.EnsureBuilt(d, level);
                var start = DungeonLevelPlan.CellCentre(level == 0 ? 9 : Enumerable.Range(0, 64).First(i => (plan.CellByIndex(i) & 0xf0) == 0));
                var (su, sv) = Dungeons.Stand(plan, start.u, start.v, true);
                var p = Dungeons.World(d, level, su, sv, 2f);
                bool floor = Physics.Raycast(p, Vector3.down, out var hit, 4f, LayerMask.GetMask("static_solid"));
                Log($"level {level}: built; floor at ({su},{sv}) {(floor ? $"{hit.point.y:0.00} (level y {Dungeons.LevelY(level):0})" : "FAIL none")}; " +
                    string.Join(", ", plan.Features.GroupBy(f => f.Kind).Select(g => $"{g.Key} {g.Count()}")));
                yield return null;
            }
            yield return new WaitForSeconds(1f);

            e.Prepare();
            Log($"chests near: {UnityEngine.Object.FindObjectsByType<U5Chest>(FindObjectsSortMode.None).Count(x => x.transform.position.y > 4000)}");

            // a room: its monsters on the floor, then dead, then the room cleared
            var cells = e.Cells;
            int roomCell = Enumerable.Range(0, 512).FirstOrDefault(i => (cells[i] & 0xf0) == 0xf0 && !DungeonInfo.Persistent(Plugin.U5.Ovl, d, cells[i] & 0xf));
            int rl = roomCell / 64, rc = roomCell % 64, room = cells[roomCell] & 0xf;
            DungeonRunner.EnsureBuilt(d, rl);
            DungeonRunner.FillRoomNow(d, rl, rc);
            for (int i = 0; i < 8; i++)
            {
                DungeonRunner.EnsureBuilt(d, rl);
                yield return new WaitForSeconds(1f);
            }
            int tag = d * 16 + room + 1;
            var monsters = Character.GetAllCharacters().Where(ch => ch.GetComponent<ZNetView>()?.GetZDO()?.GetInt(Dungeons.RoomTag) == tag).ToList();
            float floorY = Dungeons.LevelY(rl);
            Log($"room {room} (level {rl}, cell {rc}): {monsters.Count} monsters: " +
                string.Join(", ", monsters.Select(m => $"{m.m_name} y {m.transform.position.y - floorY:0.0}")) +
                (monsters.All(m => Mathf.Abs(m.transform.position.y - floorY) < 3f) ? " - all on the floor" : " - FAIL some fell"));
            Log($"room objects: {UnityEngine.Object.FindObjectsByType<LootSpot>(FindObjectsSortMode.None).Count(x => Mathf.Abs(x.transform.position.y - floorY) < 3)} loot, " +
                $"{UnityEngine.Object.FindObjectsByType<U5Chest>(FindObjectsSortMode.None).Count(x => Mathf.Abs(x.transform.position.y - floorY) < 3)} chests");
            foreach (var m in monsters) m.SetHealth(0);
            yield return new WaitForSeconds(6f);
            DungeonRunner.CheckRooms(d);
            Log($"after the fight: room state {e.RoomState(room)}, cell {e.Cells[roomCell]:x2}");

            // a trigger changes its room's tiles; the level is rebuilt
            for (int i = 0; i < 512; i++)
            {
                if ((cells[i] & 0xf0) != 0xf0 && (cells[i] & 0xf0) != 0xa0) continue;
                var tr = DungeonInfo.Triggers(Dungeons.Planner.Room(d, cells[i] & 0xf)).Where(t => t.Tile != 0 && t.X1 < 11).ToList();
                if (tr.Count == 0) continue;
                int rev = e.Revision;
                e.FireTrigger(i / 64, i % 64, tr[0]);
                var plan = DungeonRunner.EnsureBuilt(d, i / 64);
                var (ou, ov) = ((i % 64 % 8) * 11, (i % 64 / 8) * 11);
                Log($"trigger {tr[0].Index} of room {cells[i] & 0xf} (level {i / 64}): revision {rev} → {e.Revision}, tile ({tr[0].X1},{tr[0].Y1}) now {plan.TileAt(ou + tr[0].X1, ov + tr[0].Y1):x2} (wanted {tr[0].Tile:x2})");
                e.ResetRoom(i / 64, i % 64);
                break;
            }

            // a secret door found
            int secret = Enumerable.Range(0, 512).FirstOrDefault(i => (e.Cells[i] & 0xf0) == 0xd0);
            if ((e.Cells[secret] & 0xf0) == 0xd0)
            {
                e.SetCell(secret / 64, secret % 64, (byte)((e.Cells[secret] & 8) + 0xe0));
                var plan = DungeonRunner.EnsureBuilt(d, secret / 64);
                Log($"secret door at level {secret / 64} cell {secret % 64}: now {e.Cells[secret]:x2}, doors on the level {plan.Features.Count(f => f.Kind == DFeatureKind.Door)}");
            }
            else Log($"{Dungeons.Name(d)} has no secret door");
            Log("done");
        }
    }
}
