using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using Bretaheim.Data;
using UnityEngine;
using Avatar = Bretaheim.Data.Avatar;
using Object = UnityEngine.Object;

namespace Bretaheim.WorldGen
{
    /// <summary>
    /// Character generation in Iolo's hut (MASTER_PLAN §5.13). A new character's Avatar is unmade
    /// (every stat 0). While anyone unmade is in the hut, Hildir stands beside the spawn point and
    /// the hut's doors are locked (and shut); talking to her plays the original's gypsy (Data/
    /// VirtueCasting: the wagon, seven card questions answered A or B, the closing words) on the
    /// original's screens, and makes the Avatar from the answers. When nobody in the hut is
    /// unmade, the doors open to the hand and Hildir is gone; she is back for the next newcomer.
    /// The server keeps Hildir; every player's machine says on their ZDO whether they are made.
    /// </summary>
    public static class IolosHut
    {
        public const string HildirName = "u5_hildir";
        /// <summary>Player bodies' ZDOs standing in for players (DevTools/HutSelfTest).</summary>
        public static readonly List<ZDO> StandIns = new List<ZDO>();
        /// <summary>Player ZDO: 0 not known yet, 1 unmade, 2 made.</summary>
        public const string MadeKey = "u5_made";
        const float Beside = 1.3f;
        static readonly System.Random random = new System.Random();
        static VirtueCasting.Art art;

        static bool Active => Britannia.Active != null;
        public static Settlements.Built Hut => Settlements.Get(Restrictions.Hut);

        public static void Install(Harmony harmony)
        {
            PrefabManager.OnVanillaPrefabsAvailable += Create;
            var interact = AccessTools.Method(typeof(Door), nameof(Door.Interact));
            if (interact != null) harmony.Patch(interact, prefix: new HarmonyMethod(typeof(IolosHut), nameof(DoorInteract_Prefix)));
            else Plugin.Log.LogWarning("Iolo's hut: Door.Interact not found");
        }

        /// <summary>Hildir as she stands in her camp, with no shop: she talks of virtue.</summary>
        static void Create()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Create;
            var prefab = PrefabManager.Instance.CreateClonedPrefab(HildirName, "Hildir");
            if (prefab == null) { Plugin.Log.LogError("Iolo's hut: no Hildir prefab"); return; }
            foreach (var t in prefab.GetComponentsInChildren<Trader>(true)) Object.DestroyImmediate(t);
            prefab.AddComponent<Gypsy>();
            PrefabManager.Instance.AddPrefab(new CustomPrefab(prefab, false));
        }

        /// <summary>Where Hildir stands: beside the spawn point, facing it.</summary>
        public static Vector3 HildirSpot(Settlements.Built hut) => hut.Origin + new Vector3(hut.Plan.SpawnX + Beside, 0.05f, hut.Plan.SpawnZ);

        public static bool InHut(Vector3 p)
        {
            var hut = Hut;
            return hut != null && Mathf.Max(Mathf.Abs(p.x - hut.Origin.x), Mathf.Abs(p.z - hut.Origin.z)) <= hut.Site.Half;
        }

        /// <summary>Someone unmade is in the hut (as this machine sees the players around).</summary>
        public static bool Locked => Active && Player.GetAllPlayers().Any(p => p != null && InHut(p.transform.position) && p.GetComponent<ZNetView>()?.GetZDO()?.GetInt(MadeKey) == 1);

        static bool DoorInteract_Prefix(Door __instance, Humanoid character, bool hold, ref bool __result)
        {
            if (hold || !Active || !InHut(__instance.transform.position) || !Locked) return true;
            // (an open door may still be shut)
            if (__instance.GetComponent<ZNetView>()?.GetZDO()?.GetInt(ZDOVars.s_state) != 0) return true;
            var me = character as Player;
            bool unmade = me != null && !AvatarStats.Mine.Made;
            character.Message(MessageHud.MessageType.Center, unmade ? "Locked! Speak first with Hildir, who waits beside thee." : "Locked! Not all here have spoken with Hildir.");
            __result = true;
            return false;
        }

        // ── the gypsy's casting ─────────────────────────────────────────────────────────────

        /// <summary>Talking to Hildir: the gypsy's casting on the original's screens (Escape leaves it, to begin again).</summary>
        public static void Cast(Player me)
        {
            if (me == null || UltimaScreenPanel.IsOpen) return;
            if (AvatarStats.Mine.Made)
            {
                me.Message(MessageHud.MessageType.Center, "\"Thy path is chosen,\" says Hildir. \"Go now, Avatar.\"");
                return;
            }
            try
            {
                if (art == null) art = new VirtueCasting.Art(Plugin.U5.Directory, Plugin.U5.Ovl);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Iolo's hut: the gypsy's files could not be read ({e.Message}); the Avatar is made as INIT.GAM has it");
                Make(me, new VirtueCasting(Plugin.U5.Ovl, Plugin.U5.InitialGame.Character(0), random), "");
                return;
            }
            var casting = new VirtueCasting(Plugin.U5.Ovl, Plugin.U5.InitialGame.Character(0), random);
            void Leave() => UltimaScreenPanel.Close();
            void Ask()
            {
                if (casting.Done)
                {
                    var end = art.Closing(out _);
                    UltimaScreenPanel.Show(end, "Space: on", () => { UltimaScreenPanel.Close(); Make(me, casting, ""); }, null);
                    return;
                }
                casting.Next();
                var q = art.Question(casting, out _);
                UltimaScreenPanel.Show(q, $"Question {casting.Asked + 1} of {VirtueCasting.Questions}: A ({VirtueCasting.Names[casting.Left]}) or B ({VirtueCasting.Names[casting.Right]})      Escape: leave",
                    null, Leave, "AB", c => { int card = casting.Answer(c); Plugin.Log.LogInfo($"Gypsy: {VirtueCasting.Names[card]}"); Ask(); });
            }
            UltimaScreenPanel.Show(art.Opening(out _), "Space: on      Escape: leave", Ask, Leave);
        }

        static void Make(Player me, VirtueCasting casting, string why)
        {
            if (me == null) return;
            var a = AvatarStats.Mine;
            casting.Apply(a);
            a.Made = true;
            a.Save(me.m_customData);
            me.GetComponent<ZNetView>()?.GetZDO()?.Set(MadeKey, 2);
            Plugin.Log.LogInfo($"Iolo's hut: {me.GetPlayerName()} is made{(why.Length > 0 ? " (" + why + ")" : "")}: STR {a.Strength} DEX {a.Dexterity} INT {a.Intelligence} MP {a.Mana}");
            me.Message(MessageHud.MessageType.Center, $"Strength {a.Strength}, dexterity {a.Dexterity}, intelligence {a.Intelligence}.");
        }

        /// <summary>Hildir (u5_hildir): hover "Hildir", E talks.</summary>
        public class Gypsy : MonoBehaviour, Hoverable, Interactable
        {
            public string GetHoverName() => "Hildir";
            public float GetHoverOffset() => 0f;
            public string GetHoverText() => Localization.instance.Localize("Hildir\n[<color=yellow><b>$KEY_Use</b></color>] Talk");
            public bool Interact(Humanoid user, bool hold, bool alt)
            {
                if (hold || user != Player.m_localPlayer) return false;
                Cast((Player)user);
                return true;
            }
            public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
        }

        // ── keeping the hut ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Every machine: the local player's ZDO says whether their Avatar is made, and an unmade
        /// player's own machine keeps the hut's doors shut. The server: Hildir stands in the hut
        /// while anyone unmade is in it, and is gone otherwise.
        /// </summary>
        public class Keeper : MonoBehaviour
        {
            float checkAt;
            readonly List<ZDO> near = new List<ZDO>();

            void Update()
            {
                if (Time.time < checkAt) return;
                checkAt = Time.time + 1f;
                if (!Active || ZNet.instance == null) return;
                var hut = Hut;
                var me = Player.m_localPlayer;
                if (me != null)
                {
                    var a = AvatarStats.Mine;
                    // with no hut there is no gypsy: the Avatar is INIT.GAM's
                    if (!a.Made && hut == null) Make(me, new VirtueCasting(Plugin.U5.Ovl, Plugin.U5.InitialGame.Character(0), random), "no hut");
                    var zdo = me.GetComponent<ZNetView>()?.GetZDO();
                    int made = AvatarStats.Mine.Made ? 2 : 1;
                    if (zdo != null && zdo.GetInt(MadeKey) != made) zdo.Set(MadeKey, made);
                    if (made == 1 && hut != null && InHut(me.transform.position)) ShutDoors();
                }
                if (ZNet.instance.IsServer() && hut != null && hut.Plan.HasSpawn) KeepHildir(hut);
                else Why($"server {ZNet.instance.IsServer()}, hut {hut != null}, spawn {hut?.Plan.HasSpawn}");
            }

            string why;

            /// <summary>Logs why Hildir is or isn't there, when that changes.</summary>
            void Why(string now)
            {
                if (now == why) return;
                why = now;
                Plugin.Log.LogInfo($"Iolo's hut keeper: {now}");
            }

            /// <summary>The hut's open doors shut while it is locked (from an unmade player's machine).</summary>
            static void ShutDoors()
            {
                foreach (var door in Object.FindObjectsByType<Door>(FindObjectsSortMode.None))
                {
                    if (!InHut(door.transform.position)) continue;
                    var nview = door.GetComponent<ZNetView>();
                    if (nview?.GetZDO() == null || nview.GetZDO().GetInt(ZDOVars.s_state) == 0) continue;
                    nview.InvokeRPC("UseDoor", true);
                }
            }

            /// <summary>The players' ZDOs (the server's own player and every peer's, and the self-test's stand-ins).</summary>
            static IEnumerable<ZDO> PlayerZdos()
            {
                foreach (var z in StandIns) if (z != null) yield return z;
                var me = Player.m_localPlayer?.GetComponent<ZNetView>()?.GetZDO();
                if (me != null) yield return me;
                foreach (var peer in ZNet.instance.GetPeers())
                {
                    var zdo = peer.m_characterID.IsNone() ? null : ZDOMan.instance.GetZDO(peer.m_characterID);
                    if (zdo != null) yield return zdo;
                }
            }

            static readonly System.Reflection.MethodInfo zoneGenerated = AccessTools.Method(typeof(ZoneSystem), "IsZoneGenerated", new[] { typeof(Vector2s) });

            /// <summary>The hut's zone is built: loaded here, or (a dedicated server only builds ghost zones for its peers) generated.</summary>
            static bool Ready(Vector2s zone)
                => ZoneSystem.instance.IsZoneLoaded(zone) || (zoneGenerated != null && (bool)zoneGenerated.Invoke(ZoneSystem.instance, new object[] { zone }));

            void KeepHildir(Settlements.Built hut)
            {
                bool wanted = PlayerZdos().Any(z => z.GetInt(MadeKey) == 1 && InHut(z.GetPosition()));
                var spot = HildirSpot(hut);
                var zone = ZoneSystem.GetZone(spot);
                near.Clear();
                ZDOMan.instance.FindSectorObjects(zone, new SimulationDistance(1, 0), near);
                int hash = HildirName.GetStableHashCode();
                var hildirs = near.Where(z => z.GetPrefab() == hash).ToList();
                var players = PlayerZdos().ToList();
                Why($"{players.Count} players ({string.Join(", ", players.Select(z => $"made {z.GetInt(MadeKey)} in hut {InHut(z.GetPosition())}"))}), {hildirs.Count} Hildir, zone {zone} ready {Ready(zone)}, prefab {ZNetScene.instance.GetPrefab(HildirName) != null}");
                if (wanted && hildirs.Count == 0)
                {
                    if (!Ready(zone)) return;
                    var prefab = ZNetScene.instance.GetPrefab(HildirName);
                    if (prefab == null) return;
                    var face = Quaternion.LookRotation(new Vector3(-1, 0, 0));
                    Object.Instantiate(prefab, spot, face);
                    Plugin.Log.LogInfo($"Iolo's hut: Hildir waits at {spot}");
                }
                else if (!wanted && hildirs.Count > 0)
                {
                    foreach (var z in hildirs)
                    {
                        if (!z.IsOwner()) z.SetOwner(ZDOMan.GetSessionID());
                        var go = ZNetScene.instance.FindInstance(z.m_uid);
                        if (go != null) ZNetScene.instance.Destroy(go);
                        else ZDOMan.instance.DestroyZDO(z);
                    }
                    Plugin.Log.LogInfo("Iolo's hut: everyone is made; Hildir is gone");
                }
            }
        }
    }
}
