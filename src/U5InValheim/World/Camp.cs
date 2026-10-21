using System.Collections.Generic;
using System.Linq;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// Hole up and camp (Action mappings: H, not the hammer): a lean-to and a campfire before
    /// the player, out in the wild (not in a settlement, not on water). The lean-to is slept
    /// under as a bed is (Sleep); the fire burns all night and is where spells will be mixed
    /// (M near a campfire, with the magic). One camp per player: pitching another moves it.
    /// </summary>
    public static class Camp
    {
        public const string LeanToName = "u5_leanto", FirePrefab = "fire_pit", CampKey = "u5_camp";
        const float LeanToAhead = 1.5f, FireAhead = 4.2f, Off = 0.3f;
        // DATA.OVL (DGROUP)
        const int SHoleUp = 0xa2c2, SCamp = 0xa306;

        public static void Register() => PrefabManager.OnVanillaPrefabsAvailable += Create;

        /// <summary>The lean-to: the 45° wood roof on two posts at its high edge, with a place to lie under it.</summary>
        static void Create()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Create;
            var prefab = PrefabManager.Instance.CreateClonedPrefab(LeanToName, "wood_roof_45");
            var pole = PrefabManager.Instance.GetPrefab("wood_pole2");
            if (prefab == null || pole == null) { Plugin.Log.LogError("Camp: no wood_roof_45/wood_pole2 to make a lean-to of"); return; }
            var piece = prefab.GetComponent<Piece>();
            if (piece != null) piece.m_name = "Lean-to";
            // (the roof's middle is its pivot, 1.05 m up; its high edge to the front, +z)
            foreach (float side in new[] { -0.9f, 0.9f })
            {
                var post = Visual(pole);
                post.transform.SetParent(prefab.transform, false);
                post.transform.localPosition = new Vector3(side, 0f, 0.85f);
            }
            var spot = new GameObject("attach");
            spot.transform.SetParent(prefab.transform, false);
            spot.transform.localPosition = new Vector3(0f, -0.8f, -0.1f);
            spot.transform.localRotation = Quaternion.identity;
            prefab.AddComponent<LeanTo>();
            PrefabManager.Instance.AddPrefab(new CustomPrefab(prefab, false));
        }

        /// <summary>A prefab's look and shape only (no scripts or physics body).</summary>
        static GameObject Visual(GameObject prefab)
        {
            var holder = new GameObject("u5_camp_holder");
            holder.SetActive(false);
            var copy = Object.Instantiate(prefab, holder.transform);
            for (int pass = 0; pass < 4; pass++)
                foreach (var c in copy.GetComponentsInChildren<Component>(true))
                    if (c is MonoBehaviour || c is Rigidbody) Object.DestroyImmediate(c);
            copy.transform.SetParent(null, false);
            Object.Destroy(holder);
            copy.SetActive(true);
            return copy;
        }

        /// <summary>A camp's lean-to within this distance.</summary>
        public static bool Near(Vector3 at, float within) => LeanTo.All.Any(l => l != null && Vector3.Distance(l.transform.position, at) < within + 1.5f);

        /// <summary>H: the local player pitches camp here.</summary>
        public static void Pitch(Player me)
        {
            var shape = Britannia.Active;
            if (shape == null || me == null || me.IsDead() || ZoneSystem.instance == null) return;
            var at = me.transform.position;
            var fwd = Vector3.ProjectOnPlane(me.transform.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.5f) fwd = Vector3.forward;
            // not in a settlement, not on the water, not from a boat or a ladder
            bool town = Settlements.All.Any(b => Mathf.Max(Mathf.Abs(at.x - b.Origin.x), Mathf.Abs(at.z - b.Origin.z)) < b.Site.Half + b.Site.Blend);
            var leanAt = at + fwd * LeanToAhead;
            var fireAt = at + fwd * FireAhead;
            float leanY = ZoneSystem.instance.GetGroundHeight(leanAt), fireY = ZoneSystem.instance.GetGroundHeight(fireAt);
            if (town || me.IsSwimming() || me.IsAttached() || leanY < Britannia.SeaLevel + 0.3f || fireY < Britannia.SeaLevel + 0.3f || Mathf.Abs(leanY - fireY) > 2f)
            {
                me.Message(MessageHud.MessageType.Center, "Not here!");
                Plugin.Log.LogInfo($"Camp refused at {at}: town {town}, swimming {me.IsSwimming()}, attached {me.IsAttached()}, ground {leanY:0.0}/{fireY:0.0} (sea {Britannia.SeaLevel:0.0})");
                return;
            }
            Strike(me);
            leanAt.y = leanY;
            fireAt.y = fireY;
            // the lean-to opens toward the fire
            var face = Quaternion.LookRotation(fwd);
            var leanTo = Place(LeanToName, leanAt + Vector3.up * 1.05f, face, me);
            var fire = Place(FirePrefab, fireAt, Quaternion.identity, me);
            if (leanTo == null || fire == null) return;
            me.m_customData[CampKey] = Id(leanTo) + ";" + Id(fire);
            me.Message(MessageHud.MessageType.Center, (Plugin.U5.Ovl.CStr(SHoleUp) + Plugin.U5.Ovl.CStr(SCamp)).Replace("\n", " ").Trim());
            Plugin.Log.LogInfo($"{me.GetPlayerName()} pitches camp at {at}");
        }

        static GameObject Place(string prefabName, Vector3 at, Quaternion rot, Player owner)
        {
            var prefab = ZNetScene.instance.GetPrefab(prefabName);
            if (prefab == null) { Plugin.Log.LogWarning($"Camp: no {prefabName}"); return null; }
            var go = Object.Instantiate(prefab, at, rot);
            var zdo = go.GetComponent<ZNetView>()?.GetZDO();
            if (zdo != null)
            {
                // part of the world (no wear, no hammer), the fire burning all night
                zdo.Set(Settlements.FixedKey, true);
                zdo.Set(CampKey, owner.GetPlayerID());
                var wnt = go.GetComponent<WearNTear>();
                if (wnt != null) FixedPieces.Apply(wnt, zdo);
            }
            return go;
        }

        static string Id(GameObject go)
        {
            var id = go.GetComponent<ZNetView>()?.GetZDO()?.m_uid ?? ZDOID.None;
            return id.UserID + ":" + id.ID;
        }

        /// <summary>The player's last camp is struck (one camp each).</summary>
        static void Strike(Player me)
        {
            if (!me.m_customData.TryGetValue(CampKey, out var ids)) return;
            foreach (var s in ids.Split(';'))
            {
                var parts = s.Split(':');
                if (parts.Length != 2 || !long.TryParse(parts[0], out long user) || !uint.TryParse(parts[1], out uint id)) continue;
                var zdo = ZDOMan.instance.GetZDO(new ZDOID(user, id));
                if (zdo == null) continue;
                var view = ZNetScene.instance.FindInstance(zdo);
                zdo.SetOwner(ZDOMan.GetSessionID());
                if (view != null) ZNetScene.instance.Destroy(view.gameObject);
                else ZDOMan.instance.DestroyZDO(zdo);
            }
            me.m_customData.Remove(CampKey);
        }

        /// <summary>The H key (on the plugin, for the local player).</summary>
        public class Keys : MonoBehaviour
        {
            void Update()
            {
                if (!ZInput.GetKeyDown(KeyCode.H) || Britannia.Active == null) return;
                var me = Player.m_localPlayer;
                if (me == null || Busy()) return;
                Pitch(me);
            }

            static bool Busy() => (Chat.instance != null && Chat.instance.HasFocus()) || Console.IsVisible() || TextInput.IsVisible()
                                  || Menu.IsVisible() || InventoryGui.IsVisible() || Minimap.IsOpen() || TalkPanel.IsOpen
                                  || StoreGui.IsVisible() || GameCamera.InFreeFly();
        }
    }

    /// <summary>A camp's lean-to: lie down under it (E) as in a bed, by night or by day.</summary>
    public class LeanTo : MonoBehaviour, Hoverable, Interactable, ITalker
    {
        // DATA.OVL (DGROUP): "For how many hours? (1-9) "
        const int SHowManyHours = 0xa32c;
        public static readonly List<LeanTo> All = new List<LeanTo>();
        Transform spot;

        void Awake()
        {
            All.Add(this);
            spot = transform.Find("attach") ?? transform;
        }

        void OnDestroy() => All.Remove(this);

        /// <summary>"For how many hours? (1-9)", as the original's camp asks; then lie down.</summary>
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !(user is Player p) || p != Player.m_localPlayer || p.IsAttached()) return false;
            var answers = Enumerable.Range(1, 9).Select(h => (h.ToString(), (System.Action)(() =>
            {
                if (p == null || p.IsDead() || p.IsAttached()) return;
                Sleep.SetHours(p, h);
                Sleep.LieDown(p, spot, gameObject);
            }))).ToArray();
            TalkPanel.Ask(this, "Lean-to", Plugin.U5.Ovl.CStr(SHowManyHours).Trim(), answers);
            return true;
        }

        // (asked through the talk panel)
        int ITalker.Location => 0;
        int ITalker.Slot => 0;
        U5InValheim.Data.TlkScript ITalker.Script => null;
        public void EndTalk() { }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
        public string GetHoverText() => Localization.instance.Localize("Lean-to\n[<color=yellow><b>$KEY_Use</b></color>] Sleep");
        public string GetHoverName() => "Lean-to";
        public float GetHoverOffset() => 0f;
    }
}
