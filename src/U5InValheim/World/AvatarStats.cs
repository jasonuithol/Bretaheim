using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using U5InValheim.Data;
using UnityEngine;
using Avatar = U5InValheim.Data.Avatar;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// The player as an Ultima character in Britannia (MASTER_PLAN §5.6, §5.9): hit points are 30
    /// a level on the creatures' scale (Valheim's food no longer raises them), kills earn the
    /// original's experience, Lord British's apparition raises the level on waking when it is
    /// due, their blows are their weapon's attack value and their armour the defence of what they
    /// wear; the Tab screen shows the Ultima stats instead of Valheim's skills (Z-stats).
    /// </summary>
    public static class AvatarStats
    {
        const string ExperienceRpc = "U5_Experience";
        static readonly System.Random random = new System.Random();

        static Player Me => Player.m_localPlayer;
        static bool Active => Britannia.Active != null;

        public static Avatar Mine => Me != null ? Avatar.Load(Me.m_customData) : new Avatar();

        public static void Install(Harmony harmony)
        {
            Patch(harmony, typeof(Player), "SetMaxHealth", prefix: nameof(SetMaxHealth_Prefix));
            Patch(harmony, typeof(Player), "SetSleeping", postfix: nameof(SetSleeping_Postfix));
            Patch(harmony, typeof(Character), "OnDeath", prefix: nameof(OnDeath_Prefix));
            Patch(harmony, typeof(SkillsDialog), "Setup", postfix: nameof(SkillsSetup_Postfix));
            Patch(harmony, typeof(Game), "Start", postfix: nameof(GameStart_Postfix));
            Patch(harmony, typeof(Player), "RPC_UseStamina", prefix: nameof(UseStamina_Prefix));
            Patch(harmony, typeof(Bed), "Interact", prefix: nameof(BedInteract_Prefix));
            Patch(harmony, typeof(Bed), "GetHoverText", prefix: nameof(BedHover_Prefix));
            Patch(harmony, typeof(Hud), "UpdateHealth", postfix: nameof(UpdateHealth_Postfix));
            var showText = AccessTools.Method(typeof(DamageText), "ShowText", new[] { typeof(DamageText.TextType), typeof(Vector3), typeof(float), typeof(bool) });
            if (showText != null) harmony.Patch(showText, prefix: new HarmonyMethod(typeof(AvatarStats), nameof(ShowText_Prefix)));
            else Plugin.Log.LogWarning("Avatar stats: DamageText.ShowText not found");
        }

        static void Patch(Harmony h, Type t, string m, string prefix = null, string postfix = null)
        {
            var method = AccessTools.Method(t, m, t == typeof(Player) && m == "SetMaxHealth" ? new[] { typeof(float), typeof(bool) } : null);
            if (method == null) { Plugin.Log.LogWarning($"Avatar stats: {t.Name}.{m} not found"); return; }
            h.Patch(method, prefix: prefix != null ? new HarmonyMethod(typeof(AvatarStats), prefix) : null,
                postfix: postfix != null ? new HarmonyMethod(typeof(AvatarStats), postfix) : null);
        }

        /// <summary>In Britannia a player's hit points are their level's (30 a level), whatever they have eaten.</summary>
        static void SetMaxHealth_Prefix(Player __instance, ref float health)
        {
            if (!Active || __instance != Me) return;
            health = Mine.MaxHitPoints * Ultima.Scale;
        }

        /// <summary>Hit points are shown as Ultima counts them (the health bars run on the creatures' scale): 30 at level 1.</summary>
        static void UpdateHealth_Postfix(Hud __instance, Player player)
        {
            if (Active && player != null) __instance.m_healthText.text = Mathf.CeilToInt(player.GetHealth() / Ultima.Scale).ToString();
        }

        /// <summary>Damage and healing numbers in Ultima's hit points too.</summary>
        static void ShowText_Prefix(ref float dmg) { if (Active) dmg /= Ultima.Scale; }

        /// <summary>No stamina in Britannia (Ultima has none): running, jumping, swinging and swimming cost nothing.</summary>
        static bool UseStamina_Prefix() => !Active;

        // ── beds ────────────────────────────────────────────────────────────────────────────

        static bool TownBed(Bed bed) => Active && bed.GetComponentInParent<U5Fixed>() != null;

        /// <summary>The townsfolk's beds are theirs: not to be claimed or slept in, but for a room paid for at the inn (Inns).</summary>
        static bool BedInteract_Prefix(Bed __instance, Humanoid human, bool repeat, ref bool __result)
        {
            if (!TownBed(__instance)) return true;
            __result = false;
            if (repeat) return false;
            if (human is Player p && p == Player.m_localPlayer)
            {
                if (Inns.MayRest(p, __instance, out string why))
                {
                    Sleep.LieDown(p, __instance.m_spawnPoint != null ? __instance.m_spawnPoint : __instance.transform, __instance.gameObject);
                    return false;
                }
                if (why != null) Plugin.Log.LogInfo($"Inn bed refused: {why}");
            }
            human.Message(MessageHud.MessageType.Center, "Not thy bed!");
            return false;
        }

        static bool BedHover_Prefix(Bed __instance, ref string __result)
        {
            if (!TownBed(__instance)) return true;
            var me = Player.m_localPlayer;
            __result = Localization.instance.Localize(__instance.GetHoverName()
                + (me != null && Inns.MayRest(me, __instance) ? "\n[<color=yellow><b>$KEY_Use</b></color>] Sleep" : ""));
            return false;
        }

        // ── blows ───────────────────────────────────────────────────────────────────────────

        /// <summary>A player's blow on an Ultima creature: a roll of 1..their weapon's attack value (bare hands, or a weapon with no Ultima counterpart, 1).</summary>
        public static int WeaponAttack(Player player)
        {
            var weapon = player.GetCurrentWeapon();
            var name = weapon?.m_dropPrefab?.name;
            int id = name != null && weapon != player.m_unarmedWeapon?.m_itemData ? ShopTrading.Items.EquipmentOf(name) : -1;
            int attack = id >= 0 ? Plugin.U5.Ovl.ItemAttack(id) : 1;
            return Ultima.WeaponRoll(Math.Max(1, attack), random);
        }

        /// <summary>The player's armour: the defence of everything they wear that has an Ultima counterpart.</summary>
        public static int Defence(Player player)
        {
            var worn = player.GetInventory().GetEquippedItems().Where(i => i.m_dropPrefab != null).Select(i => ShopTrading.Items.EquipmentOf(i.m_dropPrefab.name));
            // In Sanct's protection ('P'): +3
            return Avatar.Defence(Plugin.U5.Ovl, worn) + (SpellStatus.Has(player, SpellStatus.Protection) ? Magic.ProtectionDefence : 0);
        }

        // ── experience and levels ───────────────────────────────────────────────────────────

        /// <summary>An Ultima creature killed by a player: its experience to the one who struck last (on their own machine).</summary>
        static void OnDeath_Prefix(Character __instance)
        {
            var victim = UltimaCreatures.Of(__instance);
            if (victim == null || __instance.GetComponent<TownNpc>() != null) return;
            var hit = Traverse.Create(__instance).Field("m_lastHit").GetValue<HitData>();
            if (!(hit?.GetAttacker() is Player killer)) return;
            int exp = Avatar.ExperienceFor(victim);
            if (killer == Me) Earn(exp);
            else ZRoutedRpc.instance?.InvokeRoutedRPC(killer.GetZDOID().UserID, ExperienceRpc, exp);
        }

        static void GameStart_Postfix() => ZRoutedRpc.instance?.Register<int>(ExperienceRpc, (sender, exp) => Earn(exp));

        static void Earn(int exp)
        {
            if (Me == null || !Active) return;
            var a = Mine;
            a.Experience = Math.Min(9999, a.Experience + exp);
            a.Save(Me.m_customData);
            Me.Message(MessageHud.MessageType.TopLeft, $"+{exp} experience");
        }

        /// <summary>Waking from sleep: when experience has earned a level, Lord British's apparition grants it (OUTSUBS apparition_level_up).</summary>
        static void SetSleeping_Postfix(Player __instance, bool sleep)
        {
            if (sleep || !Active || __instance != Me) return;
            var a = Mine;
            int which = a.LevelUp(random);
            if (which < 0) return;
            a.Save(Me.m_customData);
            Me.SetMaxHealth(a.MaxHitPoints * Ultima.Scale, true);
            Me.SetHealth(a.MaxHitPoints * Ultima.Scale);
            Me.Message(MessageHud.MessageType.Center, Plugin.U5.Ovl.CStr(0x7750).Trim() + "\n" + Avatar.LevelUpWords(Plugin.U5.Ovl, Me.GetPlayerName(), a.Level, which));
            Plugin.Log.LogInfo($"{Me.GetPlayerName()} is now level {a.Level}");
        }

        // ── Z-stats (Tab) ───────────────────────────────────────────────────────────────────

        /// <summary>The Tab screen's skill list shows the Avatar instead: strength, dexterity, intelligence, mana, hit points, level, experience, karma.</summary>
        static void SkillsSetup_Postfix(SkillsDialog __instance, Player player)
        {
            if (!Active || player != Me) return;
            var elements = Traverse.Create(__instance).Field("m_elements").GetValue<List<GameObject>>();
            if (elements == null || elements.Count == 0) return;
            var a = Mine;
            int karma = player.m_customData.TryGetValue("u5_karma", out var k) && int.TryParse(k, out int kv) ? kv : 50;
            int next = a.Level >= Avatar.MaxLevel ? a.Experience : 100 << (a.Level - 1);
            var rows = new (string name, string value, float bar, string tip)[]
            {
                ("Strength", a.Strength.ToString(), a.Strength / 30f, "Ultima strength (up to 30)"),
                ("Dexterity", a.Dexterity.ToString(), a.Dexterity / 30f, "Ultima dexterity (up to 30)"),
                ("Intelligence", a.Intelligence.ToString(), a.Intelligence / 30f, "Ultima intelligence (up to 30)"),
                ("Magic", a.Mana.ToString(), a.Intelligence > 0 ? a.Mana / (float)a.Intelligence : 0, "Mana for spells"),
                ("Hit points", $"{Mathf.CeilToInt(player.GetHealth() / Ultima.Scale)}/{a.MaxHitPoints}", player.GetHealth() / Mathf.Max(1, a.MaxHitPoints * Ultima.Scale), "30 a level"),
                ("Level", a.Level.ToString(), a.Level / (float)Avatar.MaxLevel, "Lord British's apparition raises it when thou sleepest, once thy deeds have earned it"),
                ("Experience", a.Experience.ToString(), next > 0 ? Mathf.Clamp01(a.Experience / (float)next) : 1, a.Level >= Avatar.MaxLevel ? "" : $"Level {a.Level + 1} at {next}"),
                ("Karma", karma.ToString(), karma / 99f, "Thy virtue"),
            };
            for (int i = 0; i < elements.Count; i++)
            {
                var row = elements[i];
                if (i >= rows.Length) { row.SetActive(false); continue; }
                row.SetActive(true);
                var (name, value, bar, tip) = rows[i];
                Utils.FindChild(row.transform, "name").GetComponent<TMP_Text>().text = name;
                Utils.FindChild(row.transform, "leveltext").GetComponent<TMP_Text>().text = value;
                Utils.FindChild(row.transform, "bonustext").gameObject.SetActive(false);
                Utils.FindChild(row.transform, "levelbar_total").GetComponent<GuiBar>().SetValue(bar);
                Utils.FindChild(row.transform, "levelbar").GetComponent<GuiBar>().SetValue(bar);
                Utils.FindChild(row.transform, "currentlevel").GetComponent<GuiBar>().SetValue(0);
                row.GetComponentInChildren<UITooltip>()?.Set(name, tip, __instance.m_tooltipAnchor);
            }
            __instance.m_totalSkillText.text = $"<color=orange>Level {a.Level}</color>";
        }
    }
}
