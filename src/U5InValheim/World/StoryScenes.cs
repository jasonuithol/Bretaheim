using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using U5InValheim.Data;
using U5InValheim.WorldGen.Core;
using UnityEngine;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// Plays one of the original's cut-scenes (Data/CutScene: Blackthorn's dungeon, Lord British's
    /// prison) on the whole-screen panel: the map window and the text beside it redrawn as the
    /// figures move, Space at the original's key waits, a typed line for "Your response?", Y or N
    /// for Lord British's question, the end-story pictures. Only the local player sees it.
    /// </summary>
    public static class ScenePlayer
    {
        static FixedFont font;
        public static bool Playing { get; private set; }

        public static FixedFont Font => font ?? (font = new FixedFont(File.ReadAllBytes(U5Data.FindFile(Plugin.U5.Directory, "IBM.CH"))));

        public static byte[] Read(string file)
        {
            var path = U5Data.FindFile(Plugin.U5.Directory, file);
            return path != null ? File.ReadAllBytes(path) : null;
        }

        public static IEnumerator Play(IEnumerable<Beat> beats, CutScene view, string title, Action<string> typed, Action<bool> yesNo)
        {
            Playing = true;
            var tiles = Plugin.U5.Tiles;
            // what the panel shows now: 0 nothing yet, 1 the plain scene, 2 a stop with buttons, 3 the endless wander
            int mode = 0;
            bool next = false;
            using (var e = beats.GetEnumerator())
            {
                while (true)
                {
                    bool more;
                    try { more = e.MoveNext(); }
                    catch (Exception ex) { Plugin.Log.LogError($"{title}: {ex}"); break; }
                    var me = Player.m_localPlayer;
                    if (!more || me == null || me.IsDead() || (mode > 0 && !UltimaScreenPanel.IsOpen)) break;
                    var b = e.Current;
                    var screen = b.Kind == BeatKind.Picture ? b.Picture : view.Render(tiles, Font);
                    if (b.Kind == BeatKind.Wait)
                    {
                        if (b.Endless)
                        {
                            if (mode != 3) { next = false; UltimaScreenPanel.Show(screen, $"{title}      Space: leave", () => next = true, null); mode = 3; }
                            else UltimaScreenPanel.Redraw(screen);
                            if (next) break;
                        }
                        else if (mode != 1) { UltimaScreenPanel.Show(screen, title, null, null); mode = 1; }
                        else UltimaScreenPanel.Redraw(screen);
                        if (b.Seconds > 0) yield return new WaitForSeconds(b.Seconds);
                        else yield return null;
                        continue;
                    }
                    mode = 2;
                    switch (b.Kind)
                    {
                        case BeatKind.Key:
                        case BeatKind.Picture:
                            next = false;
                            UltimaScreenPanel.Show(screen, b.Endless ? $"{title}      Space: the end" : $"{title}      Space: on", () => next = true, null);
                            while (!next && UltimaScreenPanel.IsOpen && Player.m_localPlayer != null) yield return null;
                            break;
                        case BeatKind.Ask:
                            string answer = null;
                            UltimaScreenPanel.Ask(screen, b.Prompt, t => answer = t);
                            while (answer == null && UltimaScreenPanel.IsOpen && Player.m_localPlayer != null) yield return null;
                            typed?.Invoke(answer ?? "");
                            break;
                        case BeatKind.YesNo:
                            char letter = '\0';
                            UltimaScreenPanel.Show(screen, $"{title}      Y or N", null, null, "YN", c => letter = c);
                            while (letter == '\0' && UltimaScreenPanel.IsOpen && Player.m_localPlayer != null) yield return null;
                            yesNo?.Invoke(letter == 'Y');
                            break;
                    }
                    if (b.Endless) break;
                }
            }
            UltimaScreenPanel.Close();
            Playing = false;
        }
    }

    /// <summary>
    /// Arrested in Blackthorn's palace (TOWN guard_arrest at location 18: no "Wilt thou come
    /// quietly?"): the interrogation of BLCKTHRN (Data/BlackthornScene). The party is the
    /// players within a tile (32 m); its second is the companion Blackthorn threatens, and who
    /// dies (on their own machine, a Valheim death) when he has them killed. A mantra given
    /// betrays its shrine (Shrines.Desecrate, 5 karma). The prisoner is left in the palace's
    /// dungeon at (10, 7), their keys taken.
    /// </summary>
    public static class BlackthornCapture
    {
        const string ExecuteRpc = "U5_Executed";
        public const int Palace = BlackthornScene.Location;

        public static void Install(Harmony harmony)
        {
            var start = AccessTools.Method(typeof(Game), "Start");
            if (start != null) harmony.Patch(start, postfix: new HarmonyMethod(typeof(BlackthornCapture), nameof(GameStart_Postfix)));
        }

        static void GameStart_Postfix() => ZRoutedRpc.instance?.Register<bool>(ExecuteRpc, Executed);

        public static bool Available => ScenePlayer.Read("MISCMSG.DAT") != null && ScenePlayer.Read("MISCMAPS.DAT") != null && ScenePlayer.Read("IBM.CH") != null;

        /// <summary>The party of a player: the other living players within a tile (32 m), nearest first.</summary>
        public static List<Player> Companions(Player me)
            => Player.GetAllPlayers().Where(p => p != null && p != me && !p.IsDead() && Vector3.Distance(p.transform.position, me.transform.position) < 32f)
                .OrderBy(p => Vector3.Distance(p.transform.position, me.transform.position)).Take(5).ToList();

        /// <summary>Where the prisoner is left: the palace's dungeon at (10, 7).</summary>
        public static Vector3? Cell()
        {
            var palace = Settlements.Get(Palace);
            if (palace == null) return null;
            var (x, y, z) = palace.Plan.TilePos(BlackthornScene.ExitZ, BlackthornScene.ExitY * SettlementPlan.Size + BlackthornScene.ExitX);
            return palace.Origin + new Vector3(x, y + 0.2f, z);
        }

        /// <summary>Taken by Blackthorn's guards (on the prisoner's machine): false when the scene cannot be played (the arrest goes on as elsewhere).</summary>
        public static bool Begin(Player me)
        {
            if (me == null || me != Player.m_localPlayer || ScenePlayer.Playing || !Available) return false;
            Crime.Clear(me);
            Plugin.Instance.StartCoroutine(Run(me));
            return true;
        }

        static IEnumerator Run(Player me)
        {
            var party = Companions(me);
            var ruined = Enumerable.Range(0, 8).Select(Shrines.IsRuined).ToArray();
            bool female = me.GetComponent<VisEquipment>()?.GetModelIndex() == 1;
            var scene = new BlackthornScene(Plugin.U5.Ovl, ScenePlayer.Read("MISCMSG.DAT"), ScenePlayer.Read("MISCMAPS.DAT"), me.GetPlayerName(), female,
                1 + party.Count, party.Count > 0 ? party[0].GetPlayerName() : "", ruined);
            scene.OnBetray = v =>
            {
                Shrines.Desecrate(v);
                KarmaDeeds.Change(me, -5, $"the mantra of {scene.VirtueName(v)} given to Blackthorn");
                Plugin.Log.LogInfo($"Blackthorn: {me.GetPlayerName()} gave up the mantra of {scene.VirtueName(v)}");
            };
            scene.OnExecute = sliced =>
            {
                var victim = party.FirstOrDefault(p => p != null && !p.IsDead());
                long owner = victim != null ? victim.GetZDOID().UserID : 0;
                if (owner != 0) ZRoutedRpc.instance?.InvokeRoutedRPC(owner, ExecuteRpc, sliced);
                Plugin.Log.LogInfo($"Blackthorn: {victim?.GetPlayerName() ?? "nobody"} executed ({(sliced ? "the pendulum" : "a merciful death")})");
            };
            Plugin.Log.LogInfo($"Blackthorn: {me.GetPlayerName()} is taken (party of {1 + party.Count}; asked {(scene.Virtue >= 0 ? scene.VirtueName(scene.Virtue) : "nothing: every shrine is betrayed")})");
            // dragged away: down to the dungeon at once (the scene hides it)
            var cell = Cell();
            if (cell != null) me.TeleportTo(cell.Value, Quaternion.Euler(0, 180f, 0), false);
            yield return ScenePlayer.Play(scene.Run(), scene.View, "Blackthorn's palace", a => scene.Answer = a, null);
            if (Player.m_localPlayer != me || me.IsDead()) yield break;
            cell = Cell();
            if (cell != null && Vector3.Distance(me.transform.position, cell.Value) > 3f) me.TeleportTo(cell.Value, Quaternion.Euler(0, 180f, 0), false);
            // the keys are taken (g_keys = 0)
            var key = UltimaItems.Map.Special("Key")?.Prefab;
            if (key != null)
                foreach (var item in me.GetInventory().GetAllItems().Where(i => i.m_dropPrefab != null && i.m_dropPrefab.name == key).ToList())
                    me.GetInventory().RemoveItem(item);
            Plugin.Log.LogInfo($"Blackthorn: {me.GetPlayerName()} is left in the dungeon (betrayed {scene.Betrayed}, companion killed {scene.CompanionKilled})");
        }

        /// <summary>On the companion's machine: Blackthorn has them killed.</summary>
        static void Executed(long sender, bool sliced)
        {
            var me = Player.m_localPlayer;
            if (me == null || me.IsDead()) return;
            var msg = ScenePlayer.Read("MISCMSG.DAT");
            if (msg != null) me.Message(MessageHud.MessageType.Center, CutScene.StrAt(msg, sliced ? BlackthornScene.MPendulum : BlackthornScene.MMerciful).Trim('"', ' '));
            var hit = new HitData { m_point = me.GetCenterPoint(), m_dodgeable = false, m_blockable = false };
            hit.m_damage.m_damage = 1e7f;
            me.Damage(hit);
            Plugin.Log.LogInfo($"Blackthorn: {me.GetPlayerName()} executed");
        }
    }

    /// <summary>
    /// The end of the quest (ENDGAME; MASTER_PLAN Phase 7): stepping into the mirror of Doom's
    /// last room (DUNGEON.CBT room 15 of Doom, tile 0x9d at (5, 1): "is absorbed!") brings the
    /// player to Lord British's prison (Data/EndgameScene) with the players within a tile. With
    /// the Sandalwood Box and Yes, they go home through the Orb's moongate: the end-story
    /// screens (Blackthorn's exile among them), the certificate; the world key u5_lb_freed holds
    /// the day it happened, the box is spent and they stand in Lord British's throne room.
    /// Without it they are back before the mirror when they leave the cell. Once Lord British is
    /// freed Blackthorn is gone from his palace (the ending's exile).
    /// </summary>
    public static class Endgame
    {
        public const string FreedKey = "u5_lb_freed";
        /// <summary>
        /// Where the mirror of Doom's last room stands in the world, null while Doom is not built:
        /// set by whoever builds Doom (the Underworld). A player walking into it is absorbed.
        /// </summary>
        public static Func<Vector3?> DoomMirror;
        /// <summary>How near the mirror a player is absorbed (the tile in front of it, 2 m).</summary>
        public const float Reach = 1.5f;

        /// <summary>Lord British is freed (the date, "4-5-139"), or null.</summary>
        public static string FreedOn => WorldKeys.Get(FreedKey);
        public static bool Freed => FreedOn != null;

        public static bool Available => EndStory.Available(Plugin.U5.Directory);

        public static bool HasBox(Player me)
        {
            var box = UltimaItems.Map.Special("Sandalwood Box")?.Prefab;
            return box != null && me.GetInventory().GetAllItems().Any(i => i.m_dropPrefab != null && i.m_dropPrefab.name == box);
        }

        /// <summary>A player walks into the mirror (on their machine). <paramref name="box"/> overrides the box carried (tests).</summary>
        public static bool Absorb(Player me, bool? box = null)
        {
            if (me == null || me != Player.m_localPlayer || ScenePlayer.Playing || !Available) return false;
            Plugin.Instance.StartCoroutine(Run(me, box ?? HasBox(me)));
            return true;
        }

        static IEnumerator Run(Player me, bool box)
        {
            var ovl = Plugin.U5.Ovl;
            me.Message(MessageHud.MessageType.Center, me.GetPlayerName() + ovl.CStr(EndgameScene.SAbsorbed).TrimEnd('\n'));
            var back = me.transform.position;
            var members = new List<string> { me.GetPlayerName() };
            members.AddRange(BlackthornCapture.Companions(me).Select(p => p.GetPlayerName()));
            var scene = new EndgameScene(ovl, ScenePlayer.Read("MISCMAPS.DAT"), ScenePlayer.Read("ENDMSG.DAT"), members, box, new System.Random());
            var now = Calendar.Now;
            scene.Finale = () => EndStory.Finale(Plugin.U5.Directory, ovl, me.GetPlayerName(), now.Year, now.Month, now.Day);
            scene.OnWon = () =>
            {
                WorldKeys.Set(FreedKey, now.DateText);
                var boxPrefab = UltimaItems.Map.Special("Sandalwood Box")?.Prefab;
                var item = me.GetInventory().GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == boxPrefab);
                if (item != null) me.GetInventory().RemoveItem(item, 1);
                Plugin.Log.LogInfo($"Endgame: {me.GetPlayerName()} freed Lord British on {now.DateText}");
            };
            Plugin.Log.LogInfo($"Endgame: {me.GetPlayerName()} is absorbed (box {box}, party {members.Count})");
            yield return new WaitForSeconds(1f);
            yield return ScenePlayer.Play(scene.Run(), scene.View, "Lord British", null, yes => scene.Yes = yes);
            if (Player.m_localPlayer != me || me.IsDead()) yield break;
            if (scene.Won)
            {
                var home = Respawn.Point();
                if (home != null) me.TeleportTo(home.Value, Quaternion.identity, true);
                me.Message(MessageHud.MessageType.Center, Plugin.U5.Location(17)?.Name ?? "");
                Plugin.Log.LogInfo($"Endgame: {me.GetPlayerName()} is home");
            }
            else
            {
                Plugin.Log.LogInfo($"Endgame: {me.GetPlayerName()} came without the box");
                me.Message(MessageHud.MessageType.Center, "(Lord British awaits the Sandalwood Box)");
            }
        }

        /// <summary>The local player and Doom's mirror; Blackthorn gone once Lord British is freed.</summary>
        public class Watch : MonoBehaviour
        {
            float checkAt, exileAt;
            bool away = true;

            void Update()
            {
                if (Time.time < checkAt) return;
                checkAt = Time.time + 0.25f;
                if (Britannia.Active == null) return;
                if (Time.time >= exileAt) { exileAt = Time.time + 5f; if (Freed) Exile(); }
                var me = Player.m_localPlayer;
                var mirror = DoomMirror?.Invoke();
                if (me == null || me.IsDead() || mirror == null || ScenePlayer.Playing) return;
                var d = me.transform.position - mirror.Value;
                bool near = new Vector2(d.x, d.z).magnitude < Reach && Mathf.Abs(d.y) < 3f;
                // (one absorption a visit: they must step away again first)
                if (near && away) { away = false; Absorb(me); }
                else if (!near && new Vector2(d.x, d.z).magnitude > Reach + 2f) away = true;
            }

            /// <summary>Blackthorn went through the gate into exile (END.DAT): his figure leaves his palace, on whichever peer moves him.</summary>
            static void Exile()
            {
                foreach (var c in Character.GetAllCharacters())
                {
                    var npc = TownsfolkPatches.NpcOf(c);
                    if (npc == null || npc.Location != BlackthornCapture.Palace || npc.CreatureType != 14 || !npc.IsOwned) continue;
                    Plugin.Log.LogInfo("Endgame: Blackthorn has gone into exile");
                    ZNetScene.instance.Destroy(c.gameObject);
                }
            }
        }
    }

    /// <summary>"u5 quest" (the state of the quest), "u5 blackthorn" and "u5 endgame [box]" (devcommands: the scenes, for tests).</summary>
    public static class StoryCommands
    {
        public static bool Run(string[] args, Action<string> print)
        {
            if (args.Length == 0) return false;
            var me = Player.m_localPlayer;
            switch (args[0])
            {
                case "quest":
                    if (Britannia.Active == null) { print("u5 quest: only in Britannia"); return true; }
                    print(Describe(me));
                    return true;
                case "blackthorn":
                    if (!Terminal.m_cheat) print("u5 blackthorn needs devcommands");
                    else if (me == null || Britannia.Active == null) print("u5 blackthorn: only in Britannia");
                    else if (!BlackthornCapture.Begin(me)) print("u5 blackthorn: cannot play the scene now (MISCMSG.DAT / MISCMAPS.DAT / IBM.CH missing, or a scene is playing)");
                    return true;
                case "endgame":
                    if (!Terminal.m_cheat) print("u5 endgame needs devcommands");
                    else if (me == null || Britannia.Active == null) print("u5 endgame: only in Britannia");
                    else if (!Endgame.Absorb(me, args.Length > 1 && args[1] == "box" ? true : (bool?)null)) print("u5 endgame: cannot play the ending now (END files missing, or a scene is playing)");
                    return true;
            }
            return false;
        }

        public static string Describe(Player me)
        {
            var virtues = Plugin.U5.Ovl.Virtues;
            var ruined = Enumerable.Range(0, 8).Where(Shrines.IsRuined).Select(i => virtues[i]).ToList();
            var doomed = Enumerable.Range(0, 3).Where(Shadowlords.Doomed).Select(Shadowlords.Name).ToList();
            string shards = me == null ? "" : string.Join(", ", new[] { "Shard of Falsehood", "Shard of Hatred", "Shard of Cowardice" }
                .Where(s => UltimaItems.Map.Special(s) is ItemDef d && me.GetInventory().GetAllItems().Any(i => i.m_dropPrefab?.name == d.Prefab)));
            return $"Shrines ruined: {(ruined.Count > 0 ? string.Join(", ", ruined) : "none")}\n"
                   + $"Shadowlords doomed: {(doomed.Count > 0 ? string.Join(", ", doomed) : "none")}\n"
                   + (me != null ? $"Shards carried: {(shards.Length > 0 ? shards : "none")}; the Sandalwood Box {(Endgame.HasBox(me) ? "carried" : "not carried")}\n" : "")
                   + $"Doom's mirror: {(Endgame.DoomMirror?.Invoke() is Vector3 m ? $"at ({m.x:0}, {m.y:0}, {m.z:0})" : "not built (the Underworld)")}\n"
                   + (Endgame.Freed ? $"Lord British was freed on {Endgame.FreedOn}: the quest is won." : "Lord British is a prisoner in the Underworld.");
        }
    }
}
