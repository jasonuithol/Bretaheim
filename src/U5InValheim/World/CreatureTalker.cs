using System.Linq;
using HarmonyLib;
using U5InValheim.Data;
using UnityEngine;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// One of the original's creatures living in a settlement that talks (Sin'Vraal the daemon,
    /// Grendel): its creature body (monsters.json), standing peacefully at its place (its AI off, on
    /// the players' side, so no monster, guard or ballista goes for it), with the conversation of
    /// its .NPC slot (E: Talk). The silent creatures (rats, bats, gargoyles, shadow lords) come later.
    /// </summary>
    public class CreatureTalker : MonoBehaviour, Hoverable, Interactable, ITalker
    {
        public const string TalkerKey = "u5_talker";

        ZNetView nview;
        Character body;
        Data.Location home;
        SettlementNpc npc;
        string label = "";

        public static void Install(Harmony harmony)
        {
            var awake = AccessTools.Method(typeof(Character), "Awake");
            if (awake != null) harmony.Patch(awake, postfix: new HarmonyMethod(typeof(CreatureTalker), nameof(Awake_Postfix)));
        }

        /// <summary>A creature body that is a talking settlement creature becomes one when it loads.</summary>
        static void Awake_Postfix(Character __instance)
        {
            var zdo = __instance.GetComponent<ZNetView>()?.GetZDO();
            if (zdo == null || !zdo.GetBool(TalkerKey) || __instance.GetComponent<CreatureTalker>() != null) return;
            __instance.gameObject.AddComponent<CreatureTalker>();
        }

        /// <summary>Puts a talking creature of a settlement at its place (when the settlement's zone is made).</summary>
        public static GameObject Spawn(Settlements.Built b, SettlementNpc npc, Vector3 at, float yaw)
        {
            // its creature body, on the Ultima numbers of its kind
            var c = UltimaCreatures.Spawn(npc.CreatureType, at, false);
            if (c == null) { Plugin.Log.LogWarning($"{b.Location.Name}: no body for {Plugin.U5.Ovl.CreatureName(npc.CreatureType)} (slot {npc.Slot})"); return null; }
            var go = c.gameObject;
            go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var zdo = go.GetComponent<ZNetView>().GetZDO();
            zdo.Set(TownsfolkSpawner.LocKey, b.Location.Number);
            zdo.Set(TownsfolkSpawner.SlotKey, npc.Slot);
            zdo.Set(TalkerKey, true);
            if (go.GetComponent<CreatureTalker>() == null) go.AddComponent<CreatureTalker>();
            return go;
        }

        void Awake()
        {
            nview = GetComponent<ZNetView>();
            body = GetComponent<Character>();
            var zdo = nview != null ? nview.GetZDO() : null;
            if (zdo == null) return;
            home = Plugin.U5.Location(zdo.GetInt(TownsfolkSpawner.LocKey));
            int slot = zdo.GetInt(TownsfolkSpawner.SlotKey);
            if (home == null || slot <= 0) return;
            npc = Plugin.U5.NpcsOf(home)[slot];
            label = Core.Townsfolk.Label(Plugin.U5, home, npc);
            // at peace: no hunting, on the players' side (no monster, guard or ballista goes for it)
            body.m_name = label;
            body.m_faction = Character.Faction.Players;
            foreach (var ai in GetComponents<BaseAI>()) ai.enabled = false;
            // (a flyer comes down to stand at its place)
            if (body.IsFlying()) body.Land();
        }

        public int Location => home?.Number ?? 0;
        public int Slot => npc?.Slot ?? 0;
        public TlkScript Script => npc != null ? Plugin.U5.ScriptOf(home, npc) : null;
        public string GetHoverName() => label;
        public float GetHoverOffset() => 0f;
        public string GetHoverText() => Localization.instance.Localize(label + "\n[<color=yellow><b>$KEY_Use</b></color>] Talk");
        public void EndTalk() { }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || user != Player.m_localPlayer || npc == null || body.IsDead()) return false;
            if (Script == null) { user.Message(MessageHud.MessageType.Center, "No response."); return true; }
            TalkPanel.Open(this);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
    }
}
