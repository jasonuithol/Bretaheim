using System;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using U5InValheim.Data;
using UnityEngine;
using Avatar = U5InValheim.Data.Avatar;
using Object = UnityEngine.Object;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// Casting (MASTER_PLAN §5.5, Action mappings "Cast"): a mixed spell is readied in the pouch
    /// (<see cref="PouchPanel"/>, beside the inventory; one spell at a time) and the cast key
    /// (Z by default, Magic/CastKey) casts it as CAST.OVL does: the
    /// place, a mixture spent, the mana (its circle) and the level (its circle), then the spell
    /// (<see cref="SpellEffects"/>). Scrolls are read and potions drunk with a right-click. The
    /// Avatar's mana is shown on Valheim's eitr bar (the magic bar) with the readied spell over
    /// it; six hours' sleep fills it (the camp's rule, the inn's night).
    /// </summary>
    public static class Spells
    {
        public static ConfigEntry<KeyCode> CastKey { get; private set; }

        static Spell[] table;
        public static Spell[] Table => table ?? (table = Magic.Read(Plugin.U5.Ovl));

        static Player Me => Player.m_localPlayer;
        static bool Active => Britannia.Active != null;
        static readonly System.Random random = new System.Random();
        static string S(int dg) => Plugin.U5.Ovl.CStr(dg).Replace("\n", " ").Trim();

        public static void Install(Harmony harmony)
        {
            CastKey = Plugin.Instance.Config.Bind("Magic", "CastKey", KeyCode.Z, "Casts the readied spell (readied in the pouch beside the inventory).");
            SpellStatus.Install(harmony);
            Patch(harmony, typeof(Humanoid), "UseItem", prefix: nameof(UseItem_Prefix));
            Patch(harmony, typeof(Character), "RPC_Damage", prefix: nameof(RPC_Damage_Prefix));
            Patch(harmony, typeof(Hud), "UpdateEitr", postfix: nameof(UpdateEitr_Postfix));
            Patch(harmony, typeof(Player), "SetSleeping", postfix: nameof(SetSleeping_Postfix));
            Patch(harmony, typeof(Minimap), "SetMapMode", prefix: nameof(SetMapMode_Prefix));
            Patch(harmony, typeof(Game), "Start", postfix: nameof(GameStart_Postfix));
            MixPanel.Install(harmony);
        }

        static void Patch(Harmony h, Type t, string m, string prefix = null, string postfix = null)
        {
            var method = AccessTools.Method(t, m);
            if (method == null) { Plugin.Log.LogWarning($"Spells: {t.Name}.{m} not found"); return; }
            h.Patch(method, prefix: prefix != null ? new HarmonyMethod(typeof(Spells), prefix) : null,
                postfix: postfix != null ? new HarmonyMethod(typeof(Spells), postfix) : null);
        }

        static void GameStart_Postfix() => SpellEffects.RegisterRpc();

        static void RPC_Damage_Prefix(Character __instance, HitData hit) => SpellEffects.RPC_Damage_Prefix(__instance, hit);

        // ── mana ────────────────────────────────────────────────────────────────────────────

        public static void SetMana(Player p, int mana)
        {
            var a = Avatar.Load(p.m_customData);
            a.Mana = Mathf.Clamp(mana, 0, Magic.MaxMana(a));
            a.Save(p.m_customData);
        }

        static double asleepAt = -1;

        /// <summary>Waking after six hours' sleep or more (a camp's long rest, an inn's night): the mana is full again.</summary>
        static void SetSleeping_Postfix(Player __instance, bool sleep)
        {
            if (!Active || __instance != Me || ZNet.instance == null || EnvMan.instance == null) return;
            double now = ZNet.instance.GetTimeSeconds();
            if (sleep) { if (asleepAt < 0) asleepAt = now; return; }
            if (asleepAt < 0) return;
            double hours = (now - asleepAt) / EnvMan.instance.m_dayLengthSec * 24.0;
            asleepAt = -1;
            if (hours < Magic.RestHours) return;
            var a = Avatar.Load(__instance.m_customData);
            SetMana(__instance, Magic.MaxMana(a));
        }

        static TMP_Text spellLabel;
        static readonly Action<Hud, float> SetEitrBarSize = AccessTools.MethodDelegate<Action<Hud, float>>(AccessTools.Method(typeof(Hud), "SetEitrBarSize"));

        /// <summary>Valheim's magic bar shows the Avatar's mana (Ultima's numbers), the readied spell and how many are mixed over it.</summary>
        static void UpdateEitr_Postfix(Hud __instance, Player player)
        {
            if (!Active || player == null || player != Me) return;
            var a = Avatar.Load(player.m_customData);
            int max = Math.Max(1, Magic.MaxMana(a));
            __instance.m_eitrAnimator.SetBool("Visible", true);
            __instance.m_eitrText.text = a.Mana.ToString();
            SetEitrBarSize?.Invoke(__instance, max * 8f);
            __instance.m_eitrBarSlow.SetValue(a.Mana / (float)max);
            __instance.m_eitrBarFast.SetValue(a.Mana / (float)max);
            if (spellLabel == null)
            {
                spellLabel = Object.Instantiate(__instance.m_eitrText, __instance.m_eitrBarRoot);
                spellLabel.name = "u5_spell";
                spellLabel.alignment = TextAlignmentOptions.Bottom;
                spellLabel.enableWordWrapping = false;
                var rt = spellLabel.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.anchoredPosition = new Vector2(0, 6);
                rt.sizeDelta = new Vector2(320, 24);
            }
            var pouch = PouchPanel.Of(player);
            spellLabel.text = pouch.Readied >= 0 ? $"{Table[pouch.Readied].Words} ×{pouch.SpellCount(pouch.Readied)}  ({CastKey.Value})" : "MP";
        }

        // ── casting ─────────────────────────────────────────────────────────────────────────

        /// <summary>The cast key: the readied spell, as cast_spell_command casts it.</summary>
        public static void CastReadied(Player me)
        {
            if (me.IsDead() || me.IsSleeping() || SpellStatus.Has(me, SpellStatus.Asleep)) return;
            var pouch = PouchPanel.Of(me);
            if (pouch.Readied < 0) { me.Message(MessageHud.MessageType.Center, S(SpellEffects.SNone) + " (ready a mixed spell in the pouch beside the inventory)"); return; }
            var spell = Table[pouch.Readied];
            var a = Avatar.Load(me.m_customData);
            var place = SpellEffects.PlaceOf(me);
            int location = SpellEffects.LocationAt(me.transform.position);
            string words = spell.Words + "!";
            var check = Magic.Check(spell, place, location, WearsCrown(me), pouch.SpellCount(spell.Index), a.Mana, a.Level);
            // In An: no magic works in a fight ("Absorbed!", as COMBAT's cast)
            if (check == CastCheck.Ok && place == SpellPlace.Combat && SpellEffects.Party(me).Any(p => SpellStatus.Has(p, SpellStatus.NegateMagic)))
                check = CastCheck.Absorbed;
            switch (check)
            {
                case CastCheck.Absorbed: Show(me, words, S(SpellEffects.SAbsorbed)); return;
                case CastCheck.NotHere: Show(me, words, S(SpellEffects.SNotHere)); return;
                case CastCheck.NoneMixed: Show(me, words, S(SpellEffects.SNoneMixed)); return;
            }
            pouch.TakeSpell(spell.Index);
            PouchPanel.Save(me, pouch);
            if (check == CastCheck.ManaTooLow) { Show(me, words, S(SpellEffects.SMpTooLow) + "\n" + S(SpellEffects.SFailed)); return; }
            a.Mana -= spell.Circle;
            a.Save(me.m_customData);
            if (check == CastCheck.LevelTooLow) { Show(me, words, S(SpellEffects.SFailed)); return; }
            int result = SpellEffects.Cast(me, spell.Index);
            string said = SpellEffects.TakeWords();
            if (result == SpellEffects.Succeeded) said = (said.Length > 0 ? said + "\n" : "") + S(SpellEffects.SSuccess);
            else if (result == SpellEffects.Failed) said = (said.Length > 0 ? said + "\n" : "") + S(SpellEffects.SFailed);
            Show(me, words, said);
            Plugin.Log.LogInfo($"{me.GetPlayerName()} casts {spell.Words} ({place}): {(result == 1 ? "success" : result == 0 ? "failed" : "done")}");
        }

        static void Show(Player me, string words, string said) => me.Message(MessageHud.MessageType.Center, said.Length > 0 ? words + "\n" + said : words);

        static bool WearsCrown(Player me)
        {
            var crown = UltimaItems.Map.Special("Crown of Lord British")?.Prefab;
            return crown != null && me.GetInventory().GetEquippedItems().Any(i => i.m_dropPrefab != null && i.m_dropPrefab.name == crown);
        }

        // ── scrolls and potions ──────────────────────────────────────────────────────────────

        /// <summary>Right-click (or a hotbar key) on a scroll reads it, on a potion drinks it.</summary>
        static bool UseItem_Prefix(Humanoid __instance, Inventory inventory, ItemDrop.ItemData item)
        {
            if (!Active || !(__instance is Player me) || me != Me || item?.m_dropPrefab == null) return true;
            var def = UltimaItems.Map.Get(item.m_dropPrefab.name);
            if (def == null || (def.Kind != ItemKind.Scroll && def.Kind != ItemKind.Potion)) return true;
            if (me.IsDead() || SpellStatus.Has(me, SpellStatus.Asleep)) return false;
            (inventory ?? me.GetInventory()).RemoveItem(item, 1);
            if (def.Kind == ItemKind.Scroll) SpellEffects.ReadScroll(me, def.Spell);
            else SpellEffects.Drink(me, def.Id - ItemMap.PotionBase);
            string said = SpellEffects.TakeWords();
            me.Message(MessageHud.MessageType.Center, said);
            Plugin.Log.LogInfo($"{me.GetPlayerName()} uses {def.Name}: {said.Replace("\n", " ")}");
            return false;
        }

        // ── mixing: M near a fire ───────────────────────────────────────────────────────────

        public const float FireRange = 6f;

        /// <summary>A burning fire (any Valheim fireplace: a campfire, a hearth, a brazier) near a point.</summary>
        public static bool NearFire(Vector3 at)
        {
            foreach (var area in EffectArea.GetAllAreas())
            {
                if (area == null || !area.isActiveAndEnabled || (area.m_type & EffectArea.Type.Heat) == 0) continue;
                if (Vector3.Distance(area.transform.position, at) > FireRange) continue;
                var fire = area.GetComponentInParent<Fireplace>();
                if (fire != null && fire.IsBurning()) return true;
            }
            return false;
        }

        /// <summary>By a fire in Britannia the map key mixes spells instead of opening the map (the gem shows the map).</summary>
        static bool SetMapMode_Prefix(Minimap.MapMode mode)
        {
            if (mode != Minimap.MapMode.Large || !Active || Me == null || !ZInput.GetButtonDown("Map")) return true;
            return !NearFire(Me.transform.position);
        }

        /// <summary>The keys (on the plugin, for the local player): cast, and M by a fire.</summary>
        public class Keys : MonoBehaviour
        {
            void Update()
            {
                SpellEffects.Tick();
                if (!Active) return;
                var me = Player.m_localPlayer;
                if (me == null || Busy()) return;
                if (ZInput.GetKeyDown(CastKey.Value)) CastReadied(me);
                else if (ZInput.GetButtonDown("Map") && NearFire(me.transform.position)) MixPanel.Open(me);
            }

            static bool Busy() => (Chat.instance != null && Chat.instance.HasFocus()) || global::Console.IsVisible() || TextInput.IsVisible()
                                  || Menu.IsVisible() || InventoryGui.IsVisible() || Minimap.IsOpen() || TalkPanel.IsOpen || MixPanel.IsOpen
                                  || StoreGui.IsVisible() || GameCamera.InFreeFly();
        }
    }
}
