using HarmonyLib;
using UnityEngine;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// The original's creatures living in a settlement (NPC slots with a creature sprite and no
    /// conversation: rats, bats, daemons, gargoyles, shadow lords): Ultima creatures on their
    /// numbers, behaving as their schedule's AI type says (NPC.OVL run_npc_ai): 0 stays fixed and
    /// never attacks, 6 chases the party when within 3 tiles of it, 5 and 7 chase. As in the original
    /// they live alongside the townsfolk: they only go for players, raise no alarm, and no guard
    /// or ballista goes for them.
    /// </summary>
    public class ResidentMonster : MonoBehaviour
    {
        public const string ResidentKey = "u5_resident", AiKey = "u5_resident_ai";
        /// <summary>AI 6: how far it notices the party (three tiles).</summary>
        const float Near = 6f;

        public static void Install(Harmony harmony)
        {
            var awake = AccessTools.Method(typeof(Character), "Awake");
            if (awake != null) harmony.Patch(awake, postfix: new HarmonyMethod(typeof(ResidentMonster), nameof(Awake_Postfix)));
            var isEnemy = AccessTools.Method(typeof(BaseAI), "IsEnemy", new[] { typeof(Character), typeof(Character) });
            if (isEnemy != null) harmony.Patch(isEnemy, postfix: new HarmonyMethod(typeof(ResidentMonster), nameof(IsEnemy_Postfix)));
        }

        static void Awake_Postfix(Character __instance)
        {
            var zdo = __instance.GetComponent<ZNetView>()?.GetZDO();
            if (zdo == null || zdo.GetInt(ResidentKey) == 0 || __instance.GetComponent<ResidentMonster>() != null) return;
            __instance.gameObject.AddComponent<ResidentMonster>();
        }

        /// <summary>A settlement's creature and its townsfolk leave one another alone.</summary>
        static void IsEnemy_Postfix(Character a, Character b, ref bool __result)
        {
            if (!__result || a == null || b == null) return;
            if ((Is(a) && TownsfolkPatches.NpcOf(b) != null) || (Is(b) && TownsfolkPatches.NpcOf(a) != null)) __result = false;
        }

        public static bool Is(Character c) => c != null && c.GetComponent<ResidentMonster>() != null;

        /// <summary>Puts a settlement's creature at its place (when the settlement's zone is made).</summary>
        public static GameObject Spawn(Settlements.Built b, Data.SettlementNpc npc, int ai, Vector3 at)
        {
            var c = UltimaCreatures.Spawn(npc.CreatureType, at, false);
            if (c == null) { Plugin.Log.LogWarning($"{b.Location.Name}: no body for {Plugin.U5.Ovl.CreatureName(npc.CreatureType)} (slot {npc.Slot})"); return null; }
            var zdo = c.GetComponent<ZNetView>().GetZDO();
            zdo.Set(TownsfolkSpawner.LocKey, b.Location.Number);
            zdo.Set(TownsfolkSpawner.SlotKey, npc.Slot);
            zdo.Set(ResidentKey, b.Location.Number);
            zdo.Set(AiKey, ai);
            if (c.GetComponent<ResidentMonster>() == null) c.gameObject.AddComponent<ResidentMonster>();
            return c.gameObject;
        }

        void Awake()
        {
            var zdo = GetComponent<ZNetView>()?.GetZDO();
            if (zdo == null) return;
            int ai = zdo.GetInt(AiKey);
            var monster = GetComponent<MonsterAI>();
            switch (ai)
            {
                case 0: case 1: case 2: case 3:
                    // fixed (or only wandering, fleeing): it never comes for the party
                    foreach (var a in GetComponents<BaseAI>()) a.enabled = false;
                    var body = GetComponent<Character>();
                    if (body != null && body.IsFlying()) body.Land();
                    break;
                case 4: case 6:
                    // it keeps to its place and goes for the party only when they come close
                    if (monster != null)
                    {
                        monster.m_viewRange = Near;
                        monster.m_hearRange = Near;
                        monster.m_alertRange = Near;
                        monster.m_randomMoveRange = 2f;
                    }
                    break;
                default:
                    // 5, 7: it hunts the party
                    break;
            }
        }
    }
}
