using System;
using System.Collections.Generic;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using U5InValheim.Data;
using UnityEngine;
using Object = UnityEngine.Object;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// What spells and potions leave on people and creatures (MASTER_PLAN §5.5):
    /// <list type="bullet">
    /// <item>on players, Valheim status effects of our own (asleep, protection, quickness, light,
    /// negate magic, invisibility; Valheim's own Poison is the original's 'P'), each with the
    /// icon of the status the asset mapping names (sleeping → Resting, invisible → Cold,
    /// protection → Sheltered);</item>
    /// <item>on creatures, a time in their ZDO until which they sleep, flee, stand frozen (An
    /// Tym), are charmed or confused, or serve a summoner; the owner's AI obeys it
    /// (<see cref="UpdateAI_Prefix"/>), so it holds whoever simulates them;</item>
    /// <item>the spells' sights, shown on every peer (<see cref="Fx"/>).</item>
    /// </list>
    /// </summary>
    public static class SpellStatus
    {
        public const string Asleep = "U5_Asleep", Protection = "U5_Protection", Quickness = "U5_Quickness", Light = "U5_Light",
            NegateMagic = "U5_NegateMagic", Invisible = "U5_Invisible";
        /// <summary>A player's invisibility, in their ZDO (network time it ends) for the monsters' owners to see.</summary>
        public const string InvisibleKey = "u5_invisible_until";

        public enum Creature { Sleep, Fear, Stop, Charm, Confuse, Summoned }
        static readonly string[] Keys = { "u5_sleep_until", "u5_fear_until", "u5_stop_until", "u5_charm_until", "u5_confuse_until", "u5_summon_until" };
        /// <summary>A creature turned to the players' side by us (charm, confusion, a summons), to be turned back.</summary>
        const string TurnedKey = "u5_turned";
        const string BewitchRpc = "U5_Bewitch", FxRpc = "U5_SpellFx";
        static readonly System.Random random = new System.Random();

        static readonly (string name, string shown, string icon, string tip)[] PlayerEffects =
        {
            (Asleep, "Asleep", "Resting", "Thou art asleep."),
            (Protection, "Protection", "Shelter", "In Sanct: +3 to thy defence."),
            (Quickness, "Quickness", "CorpseRun", "Rel Tym: thou movest quickly."),
            (Light, "Light", "Demister", "In Lor: a magical light."),
            (NegateMagic, "Negate magic", "Wet", "In An: no magic works."),
            (Invisible, "Invisible", "Cold", "Sanct Lor: the monsters see thee not."),
        };

        public static int Hash(string name) => name.GetStableHashCode();

        public static double Now => ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : Time.time;

        public static void Register()
        {
            foreach (var (name, shown, _, tip) in PlayerEffects)
            {
                SE_Stats se = name == Light ? ScriptableObject.CreateInstance<LightStatus>() : ScriptableObject.CreateInstance<SE_Stats>();
                se.name = name;
                se.m_name = shown;
                se.m_tooltip = tip;
                if (name == Asleep) se.m_speedModifier = -1f;
                if (name == Quickness) se.m_speedModifier = 0.5f;
                ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(se, false));
            }
            ItemManager.OnItemsRegistered += Icons;
        }

        /// <summary>Our statuses wear the icons of Valheim's (in the game's ObjectDB, once it has them).</summary>
        static void Icons()
        {
            var db = ObjectDB.instance;
            if (db == null) return;
            foreach (var (name, _, icon, _) in PlayerEffects)
            {
                var ours = db.GetStatusEffect(Hash(name));
                var theirs = db.GetStatusEffect(Hash(icon));
                if (ours != null && ours.m_icon == null && theirs != null) ours.m_icon = theirs.m_icon;
            }
        }

        public static void Install(Harmony harmony)
        {
            Patch(harmony, typeof(MonsterAI), "UpdateAI", prefix: nameof(UpdateAI_Prefix));
            Patch(harmony, typeof(BaseAI), "CanSeeTarget", postfix: nameof(Senses_Postfix), args: new[] { typeof(Character) });
            Patch(harmony, typeof(BaseAI), "CanHearTarget", postfix: nameof(Senses_Postfix), args: new[] { typeof(Character) });
            Patch(harmony, typeof(Game), "Start", postfix: nameof(GameStart_Postfix));
        }

        static void Patch(Harmony h, Type t, string m, string prefix = null, string postfix = null, Type[] args = null)
        {
            var method = AccessTools.Method(t, m, args);
            if (method == null) { Plugin.Log.LogWarning($"Spell status: {t.Name}.{m} not found"); return; }
            h.Patch(method, prefix: prefix != null ? new HarmonyMethod(typeof(SpellStatus), prefix) : null,
                postfix: postfix != null ? new HarmonyMethod(typeof(SpellStatus), postfix) : null);
        }

        static void GameStart_Postfix()
        {
            ZRoutedRpc.instance?.Register<ZDOID, int, float>(BewitchRpc, RPC_Bewitch);
            ZRoutedRpc.instance?.Register<string, Vector3, float>(FxRpc, (sender, name, at, seconds) => LocalFx(name, at, seconds));
        }

        // ── players ─────────────────────────────────────────────────────────────────────────

        /// <summary>One of our statuses on a player (on their own machine) for so many seconds.</summary>
        public static void Give(Player p, string name, float seconds)
        {
            var se = p.GetSEMan().AddStatusEffect(Hash(name), resetTime: true);
            if (se != null) se.m_ttl = seconds;
            if (name == Invisible) p.GetComponent<ZNetView>()?.GetZDO()?.Set(InvisibleKey, (float)(Now + seconds));
        }

        public static bool Has(Player p, string name) => p != null && p.GetSEMan().HaveStatusEffect(Hash(name));

        public static void Take(Player p, string name)
        {
            p.GetSEMan().RemoveStatusEffect(Hash(name), true);
            if (name == Invisible) p.GetComponent<ZNetView>()?.GetZDO()?.Set(InvisibleKey, 0f);
        }

        public static bool IsInvisible(Character c) => c is Player && (c.GetComponent<ZNetView>()?.GetZDO()?.GetFloat(InvisibleKey) ?? 0f) > Now;

        /// <summary>Monsters do not see or hear an invisible player (Sanct Lor, the black potion).</summary>
        static void Senses_Postfix(Character target, ref bool __result)
        {
            if (__result && IsInvisible(target)) __result = false;
        }

        // ── creatures ───────────────────────────────────────────────────────────────────────

        /// <summary>Casts a state on a creature: its owner keeps it in the creature's ZDO.</summary>
        public static void Bewitch(Character c, Creature kind, float seconds)
        {
            var zdo = c?.GetComponent<ZNetView>()?.GetZDO();
            if (zdo == null) return;
            if (zdo.IsOwner()) Set(c, kind, seconds);
            else ZRoutedRpc.instance?.InvokeRoutedRPC(zdo.GetOwner(), BewitchRpc, zdo.m_uid, (int)kind, seconds);
        }

        static void RPC_Bewitch(long sender, ZDOID id, int kind, float seconds)
        {
            var go = ZNetScene.instance?.FindInstance(id);
            var c = go != null ? go.GetComponent<Character>() : null;
            if (c != null) Set(c, (Creature)kind, seconds);
        }

        static void Set(Character c, Creature kind, float seconds)
        {
            var zdo = c.GetComponent<ZNetView>().GetZDO();
            zdo.Set(Keys[(int)kind], (float)(Now + seconds));
            if (kind == Creature.Charm || kind == Creature.Summoned) TurnToPlayers(c, zdo);
            if (kind == Creature.Sleep || kind == Creature.Stop) c.GetBaseAI()?.StopMoving();
        }

        static float Until(ZDO zdo, Creature kind) => zdo.GetFloat(Keys[(int)kind]);
        public static bool Under(Character c, Creature kind) => (c?.GetComponent<ZNetView>()?.GetZDO() is ZDO z) && Until(z, kind) > Now;

        static void TurnToPlayers(Character c, ZDO zdo)
        {
            zdo.Set(TurnedKey, true);
            c.SetTamed(true);
            c.m_group = "";
        }

        static void TurnBack(Character c, ZDO zdo)
        {
            zdo.Set(TurnedKey, false);
            c.SetTamed(false);
            if (UltimaCreatures.Of(c) != null) c.m_group = UltimaCreatures.MonsterGroup;
        }

        static readonly Dictionary<Character, (int turn, bool turned)> confusion = new Dictionary<Character, (int, bool)>();
        static readonly AccessTools.FieldRef<MonsterAI, Character> TargetOf = AccessTools.FieldRefAccess<MonsterAI, Character>("m_targetCreature");
        static readonly System.Reflection.MethodInfo FleeMethod = AccessTools.Method(typeof(BaseAI), "Flee");

        /// <summary>
        /// The creature's owner, each AI step: asleep or time-stopped it stands still; afraid it
        /// runs from the nearest player; charmed, confused (some turns) or summoned it is on the
        /// players' side until its time is up (a summons then goes); an invisible player is no
        /// target. Only creatures we touched carry the keys, so the rest pay one ZDO read.
        /// </summary>
        static bool UpdateAI_Prefix(MonsterAI __instance, float dt, ref bool __result)
        {
            var c = __instance.GetComponent<Character>();
            var zdo = c != null ? c.GetComponent<ZNetView>()?.GetZDO() : null;
            if (zdo == null || !zdo.IsOwner()) return true;
            double now = Now;
            var target = TargetOf(__instance);
            if (target != null && IsInvisible(target)) TargetOf(__instance) = null;
            if (Until(zdo, Creature.Sleep) > now || Until(zdo, Creature.Stop) > now)
            {
                __instance.StopMoving();
                __result = true;
                return false;
            }
            float summoned = Until(zdo, Creature.Summoned);
            if (summoned > 0 && summoned <= now)
            {
                // the summons is over: it goes as it came
                Fx("fx_summon_skeleton_spawn", c.transform.position, 3f);
                zdo.Set(Keys[(int)Creature.Summoned], 0f);
                ZNetScene.instance.Destroy(c.gameObject);
                __result = true;
                return false;
            }
            bool charmed = Until(zdo, Creature.Charm) > now || summoned > now;
            float confused = Until(zdo, Creature.Confuse);
            if (!charmed && confused > now)
            {
                // Quas An Wis: each turn a creature whose wits fail a roll of 1..30 fights for the players
                int turn = (int)(now / Magic.TurnSeconds);
                if (!confusion.TryGetValue(c, out var state) || state.turn != turn)
                    confusion[c] = state = (turn, (UltimaCreatures.Of(c)?.Skill ?? 30) < random.Next(1, 31));
                charmed = state.turned;
            }
            else confusion.Remove(c);
            bool turned = zdo.GetBool(TurnedKey);
            if (charmed && !turned) TurnToPlayers(c, zdo);
            else if (!charmed && turned) TurnBack(c, zdo);
            else if (charmed) c.m_group = "";
            if (Until(zdo, Creature.Fear) > now)
            {
                var from = Nearest(c.transform.position);
                if (from != null && FleeMethod != null)
                {
                    FleeMethod.Invoke(__instance, new object[] { dt, from.transform.position });
                    __result = true;
                    return false;
                }
            }
            return true;
        }

        static Player Nearest(Vector3 at)
        {
            Player best = null;
            float bestD = 40f;
            foreach (var p in Player.GetAllPlayers())
            {
                float d = Vector3.Distance(p.transform.position, at);
                if (d < bestD) { bestD = d; best = p; }
            }
            return best;
        }

        // ── sights ──────────────────────────────────────────────────────────────────────────

        /// <summary>A spell's sight (a Valheim effect prefab) at a place on every peer, gone after a while.</summary>
        public static void Fx(string prefab, Vector3 at, float seconds)
        {
            if (ZRoutedRpc.instance != null) ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, FxRpc, prefab, at, seconds);
            else LocalFx(prefab, at, seconds);
        }

        /// <summary>The effect drawn here only: a copy of the prefab without its network parts, so it never becomes a world object.</summary>
        public static GameObject LocalFx(string prefab, Vector3 at, float seconds)
        {
            if (!Recolour.Draws) return null;
            var p = ZNetScene.instance?.GetPrefab(prefab);
            if (p == null) return null;
            var holder = new GameObject("u5_fx_holder");
            holder.SetActive(false);
            var go = Object.Instantiate(p, at, Quaternion.identity, holder.transform);
            foreach (var c in go.GetComponentsInChildren<ZSyncTransform>(true)) Object.DestroyImmediate(c);
            foreach (var c in go.GetComponentsInChildren<ZNetView>(true)) Object.DestroyImmediate(c);
            go.transform.SetParent(null, true);
            Object.Destroy(holder);
            go.SetActive(true);
            Object.Destroy(go, seconds);
            return go;
        }
    }

    /// <summary>In Lor, Vas Lor: a light over the player for the spell's time.</summary>
    public class LightStatus : SE_Stats
    {
        public float m_range = 18f;
        GameObject lamp;

        public override void Setup(Character character)
        {
            base.Setup(character);
            if (!Recolour.Draws || character == null) return;
            lamp = new GameObject("u5_light");
            lamp.transform.SetParent(character.transform, false);
            lamp.transform.localPosition = new Vector3(0, 3f, 0);
            var light = lamp.AddComponent<UnityEngine.Light>();
            light.type = LightType.Point;
            light.range = m_range;
            light.intensity = 1.6f;
            light.color = new Color(1f, 0.93f, 0.75f);
            light.shadows = LightShadows.None;
        }

        public override void Stop()
        {
            base.Stop();
            if (lamp != null) Object.Destroy(lamp);
        }
    }
}
