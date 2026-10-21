using Bretaheim.Data;
using UnityEngine;

namespace Bretaheim.WorldGen
{
    /// <summary>
    /// Blackthorn's guards (TALK guard_challenge, location 18): a guard of Blackthorn's palace who
    /// comes up to a player challenges them. Wearing the Black Badge: "Give now the password,
    /// bearer of the Badge!", typed in the conversation panel; the right word ("IMPE…", its first
    /// four letters, any case) — "Pass, friend!" and the guards let them be until they leave the
    /// palace. Without the badge, or with the wrong word, they are to be arrested (Crime: "Wilt
    /// thou come quietly?").
    /// </summary>
    public class BlackthornGuards : MonoBehaviour
    {
        const float Reach = 3f, Again = 30f;
        float checkAt, askedAt = -999f;
        bool passed;

        void Update()
        {
            if (Time.time < checkAt) return;
            checkAt = Time.time + 0.5f;
            var me = Player.m_localPlayer;
            var palace = Settlements.Get(ItemUse.BlackthornsPalace);
            if (me == null || me.IsDead() || palace == null || Britannia.Active == null) return;
            var at = me.transform.position;
            bool inside = Mathf.Max(Mathf.Abs(at.x - palace.Origin.x), Mathf.Abs(at.z - palace.Origin.z)) < palace.Site.Half + palace.Site.Blend;
            if (!inside) { passed = false; return; }
            if (passed || TalkPanel.IsOpen || Crime.WantedBy(me) != 0 || Crime.HostileTo(me) != 0 || Time.time - askedAt < Again) return;
            foreach (var c in Character.GetAllCharacters())
            {
                var npc = TownsfolkPatches.NpcOf(c);
                if (npc == null || !npc.IsGuard || npc.IsDead || npc.Location != ItemUse.BlackthornsPalace) continue;
                if (Vector3.Distance(c.transform.position, at) > Reach) continue;
                askedAt = Time.time;
                Challenge(me, npc, palace);
                return;
            }
        }

        void Challenge(Player me, TownNpc guard, Settlements.Built palace)
        {
            var ovl = Plugin.U5.Ovl;
            bool badge = (WornEffects.Bits(me) & WornEffects.Badge) != 0;
            Plugin.Log.LogInfo($"Blackthorn's guard challenges {me.GetPlayerName()} (badge {badge})");
            if (!badge)
            {
                Crime.Commit(me, palace);
                return;
            }
            string ask = "\"" + ovl.CStr(ItemUse.SGivePassword).Replace("\n", " ").Trim() + "\"";
            TalkPanel.AskTyped(guard, guard.GetHoverName(), ask, "Your response", answer =>
            {
                if (ItemUse.PasswordMatches(ovl, answer))
                {
                    passed = true;
                    TalkPanel.Tell("\"" + ovl.CStr(ItemUse.SPassFriend).Trim() + "\"");
                    Plugin.Log.LogInfo($"{me.GetPlayerName()} gave Blackthorn's password");
                    return;
                }
                TalkPanel.Close();
                Crime.Commit(me, palace);
            });
        }
    }
}
