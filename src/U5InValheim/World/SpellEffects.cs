using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using U5InValheim.Data;
using UnityEngine;
using Avatar = U5InValheim.Data.Avatar;
using Object = UnityEngine.Object;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// The 48 spells, the eight scrolls and the eight potions in Valheim (CAST.OVL cast_spell_command's
    /// switch, use_scroll, use_potion; MASTER_PLAN §5.5). The original's combat grid becomes real
    /// time around the caster: "the party" is every player within <see cref="PartyRange"/> (one
    /// tile, as the regalia), "combat" is an Ultima monster hostile to players within
    /// <see cref="CombatRange"/>, a missile or a creature is aimed with the view (the crosshair),
    /// a direction is the way the caster looks, a wind is a cone that way. Spell hits carry the
    /// <see cref="SpellSkill"/> mark so they keep their Ultima numbers (a missile's roll less the
    /// target's armour, the undead taking it whole). Spells that need other systems call hooks
    /// (dungeons, moongates) and say "Not here!" until those are set.
    /// </summary>
    public static class SpellEffects
    {
        public const int Silent = -1, Failed = 0, Succeeded = 1;
        public const float PartyRange = 32f, CombatRange = 25f, AimRange = 40f, WindRange = 24f, WindHalfAngle = 30f, FieldRadius = 2.5f, TouchRange = 10f;
        /// <summary>The mark of a spell's hit (no staff exists in Britannia, so no other blow carries it). The target's armour counts (missiles, fields) unless m_toolTier is <see cref="NoArmour"/> (winds, the earthquake): a projectile's hit keeps the skill but not the tier.</summary>
        public const Skills.SkillType SpellSkill = Skills.SkillType.ElementalMagic;
        public const short NoArmour = 77;

        /// <summary>Hooks for systems built elsewhere: whether a point is in a dungeon; Uus/Des Por (−1 up, +1 down; true when moved); Vas Rel Por to a moongate (phase 0..7; true when gone).</summary>
        public static Func<Vector3, bool> InDungeon;
        public static Func<Player, int, bool> DungeonLadder;
        public static Func<Player, int, bool> GateTravel;
        /// <summary>A spell's effects that last this many turns where the original's last a whole fight (sleep, fear, charm, a summons).</summary>
        public const int FightTurns = 10, SummonTurns = 15, PlayerSleepTurns = 5;

        static readonly System.Random random = new System.Random();
        static readonly List<string> said = new List<string>();
        static DataOvl Ovl => Plugin.U5.Ovl;
        static string S(int dg) => Ovl.CStr(dg).Replace("\n", " ").Trim();
        static float Turns(int n) => n * Magic.TurnSeconds;
        static float DayLength => EnvMan.instance != null ? EnvMan.instance.m_dayLengthSec : 1800f;

        // DATA.OVL (DGROUP) CAST/CAST2 words
        public const int SPoof = 0x459a, SCharmed = 0x45c6, SMagicAbsorbed = 0x45f2, SNone = 0x4611, SNoEffect = 0x4618, SAbsorbed = 0x4624,
            SNotHere = 0x462f, SNoneMixed = 0x463a, SMpTooLow = 0x4647, SSuccess = 0x4656, SFailed = 0x4660, SScroll = 0x466a, SLight = 0x4673,
            SWind = 0x467b, SProtection = 0x4689, SNegateMagic = 0x4696, SView = 0x46a5, SSummonDaemon = 0x46b7, SResurrection = 0x46d2,
            SNegateTime = 0x46f8, SPotion = 0x4706, SHealed = 0x470e, SCured = 0x4717, SPoisoned = 0x4726, SSlept = 0x4731, SPotionPoof = 0x4739,
            SInvisible = 0x4740, SNoNoticeable = 0x474c, SOops = 0x9532, SNotDead = 0x953c, SDisarmed = 0x45a1, SChestOpened = 0x45ac;

        /// <summary>The words said since the last call (to show the caster).</summary>
        public static string TakeWords()
        {
            var s = string.Join("\n", said.Where(w => w.Length > 0));
            said.Clear();
            return s;
        }

        static void Say(string s) => said.Add(s);
        static void Say(int dg) => said.Add(S(dg));

        // ── the place and the people ─────────────────────────────────────────────────────────

        /// <summary>The settlement (location number) a point is in, 0 outside.</summary>
        public static int LocationAt(Vector3 at)
        {
            foreach (var b in Settlements.All)
                if (Mathf.Max(Mathf.Abs(at.x - b.Origin.x), Mathf.Abs(at.z - b.Origin.z)) < b.Site.Half) return b.Location.Number;
            return 0;
        }

        /// <summary>Where the caster is, as the original's places count it: a fight, a dungeon, a settlement or the open land.</summary>
        public static SpellPlace PlaceOf(Player me)
        {
            var at = me.transform.position;
            if (Enemies(at, CombatRange).Any()) return SpellPlace.Combat;
            if (InDungeon != null && InDungeon(at)) return SpellPlace.Dungeon;
            return LocationAt(at) != 0 ? SpellPlace.Town : SpellPlace.Overworld;
        }

        /// <summary>Ultima creatures (not townsfolk), alive, within range; enemies only: not on the players' side.</summary>
        public static IEnumerable<Character> Creatures(Vector3 at, float range)
            => Character.GetAllCharacters().Where(c => c != null && !(c is Player) && !c.IsDead() && UltimaCreatures.Of(c) != null
                                                     && c.GetComponent<TownNpc>() == null && Vector3.Distance(c.transform.position, at) <= range);

        public static IEnumerable<Character> Enemies(Vector3 at, float range) => Creatures(at, range).Where(c => !c.IsTamed());

        public static List<Player> Party(Player me)
        {
            var near = new List<Player>();
            Player.GetPlayersInRange(me.transform.position, PartyRange, near);
            if (!near.Contains(me)) near.Add(me);
            return near;
        }

        static Transform View(Player me) => GameCamera.instance != null ? GameCamera.instance.transform : me.m_eye != null ? me.m_eye : me.transform;

        /// <summary>The way the caster looks (a direction), level.</summary>
        static Vector3 Facing(Player me)
        {
            var f = Vector3.ProjectOnPlane(View(me).forward, Vector3.up);
            return f.sqrMagnitude > 0.01f ? f.normalized : me.transform.forward;
        }

        /// <summary>The creature under the crosshair: the one nearest the line of sight (within 8°) that can be seen.</summary>
        public static Character Aimed(Player me, float range, bool enemiesOnly = false)
        {
            var eye = View(me);
            Character best = null;
            float bestAngle = 8f;
            foreach (var c in enemiesOnly ? Enemies(me.transform.position, range) : Creatures(me.transform.position, range))
            {
                var to = c.GetCenterPoint() - eye.position;
                float angle = Vector3.Angle(eye.forward, to);
                if (angle >= bestAngle) continue;
                if (Physics.Raycast(eye.position, to.normalized, out var hit, to.magnitude, LayerMask.GetMask("terrain", "static_solid", "piece")) && hit.collider.GetComponentInParent<Character>() != c) continue;
                best = c;
                bestAngle = angle;
            }
            return best;
        }

        /// <summary>The ground point under the crosshair within range (or straight ahead at that range).</summary>
        static Vector3 AimPoint(Player me, float range)
        {
            var eye = View(me);
            if (Physics.Raycast(eye.position, eye.forward, out var hit, range + 10f, LayerMask.GetMask("terrain", "static_solid", "piece", "Default")) && Vector3.Distance(hit.point, me.transform.position) <= range)
                return hit.point;
            var p = me.transform.position + Facing(me) * Mathf.Min(range, 12f);
            p.y = ZoneSystem.instance != null ? ZoneSystem.instance.GetGroundHeight(p) : p.y;
            return p;
        }

        /// <summary>The player the caster looks at (within reach), or the caster: the original's "On who?".</summary>
        static Player Whom(Player me)
        {
            var eye = View(me);
            Player best = me;
            float bestAngle = 10f;
            foreach (var p in Party(me))
            {
                if (p == me || Vector3.Distance(p.transform.position, me.transform.position) > TouchRange) continue;
                float angle = Vector3.Angle(eye.forward, p.GetCenterPoint() - eye.position);
                if (angle < bestAngle) { best = p; bestAngle = angle; }
            }
            return best;
        }

        static int CasterIntelligence(Player me) => Avatar.Load(me.m_customData).Intelligence;

        /// <summary>COMSUBS target_evades: the creature's wits (stats +1) against the caster's intelligence.</summary>
        static bool Evades(Player me, Character c) => Magic.Evades(CasterIntelligence(me), UltimaCreatures.Of(c)?.Skill ?? 0, random.Next(1, 31));

        static bool Immune(Character c) => Magic.Immune(UltimaCreatures.Of(c)?.Type ?? -1);

        static void Flash(Player me, string fx = "vfx_Potion_eitr_minor") => SpellStatus.Fx(fx, me.transform.position, 3f);

        // ── hits ────────────────────────────────────────────────────────────────────────────

        public static HitData SpellHit(Player me, Character target, int damage, bool armour)
        {
            var hit = new HitData
            {
                m_point = target != null ? target.GetCenterPoint() : me.transform.position,
                m_dir = target != null ? (target.transform.position - me.transform.position).normalized : me.transform.forward,
                m_skill = SpellSkill, m_toolTier = armour ? (short)0 : NoArmour, m_dodgeable = false, m_blockable = false, m_ranged = true,
            };
            hit.m_damage.m_damage = damage * Ultima.Scale;
            hit.SetAttacker(me);
            return hit;
        }

        static void Strike(Player me, Character c, int damage, bool armour, bool poison = false, bool stagger = false)
        {
            var hit = SpellHit(me, c, damage, armour);
            if (poison) hit.m_damage.m_poison = Ultima.Scale;
            if (stagger) hit.m_staggerMultiplier = 100f;
            c.Damage(hit);
        }

        /// <summary>
        /// A spell hit arriving at its target's owner: a missile's or field's roll less a roll of
        /// the target's armour (the undead take it whole: a spell), as COMBAT.OVL counts a
        /// thrown weapon; the winds' and the earthquake's damage as it is.
        /// </summary>
        public static void RPC_Damage_Prefix(Character __instance, HitData hit)
        {
            if (hit == null || hit.m_skill != SpellSkill || hit.m_toolTier == NoArmour) return;
            var defender = UltimaCreatures.Of(__instance);
            int armour = defender != null ? defender.Armour : __instance is Player p && Britannia.Active != null ? AvatarStats.Defence(p) : 0;
            int raw = Mathf.RoundToInt(hit.m_damage.m_damage / Ultima.Scale);
            hit.m_damage.m_damage = Ultima.Damage(raw, armour, defender?.Undead ?? false, true, random) * Ultima.Scale;
        }

        // ── people (on the target's own machine) ─────────────────────────────────────────────

        public enum On { Heal, FullHeal, Cure, Awaken, Sleep, Poison, Status, Wind }
        public const string OnRpc = "U5_SpellOn";
        static readonly string[] StatusNames = { SpellStatus.Protection, SpellStatus.Quickness, SpellStatus.Light, SpellStatus.NegateMagic, SpellStatus.Invisible, SpellStatus.Asleep };

        public static void RegisterRpc() => ZRoutedRpc.instance?.Register<int, int, float>(OnRpc, (sender, kind, amount, seconds) => { var me = Player.m_localPlayer; if (me != null) ApplyHere(me, (On)kind, amount, seconds); });

        /// <summary>A spell on a player: here if it is the local player, else on their machine (assumed to take).</summary>
        static bool Apply(Player p, On kind, int amount = 0, float seconds = 0)
        {
            if (p == Player.m_localPlayer) return ApplyHere(p, kind, amount, seconds);
            var zdo = p.GetComponent<ZNetView>()?.GetZDO();
            if (zdo != null) ZRoutedRpc.instance?.InvokeRoutedRPC(zdo.GetOwner(), OnRpc, (int)kind, amount, seconds);
            return true;
        }

        static bool Status(Player p, string name, float seconds) => Apply(p, On.Status, Array.IndexOf(StatusNames, name), seconds);

        static bool ApplyHere(Player p, On kind, int amount, float seconds)
        {
            if (p.IsDead()) return false;
            switch (kind)
            {
                case On.Heal:
                    p.Heal(amount * Ultima.Scale, true);
                    SpellStatus.Fx("vfx_Potion_health_medium", p.transform.position, 3f);
                    return true;
                case On.FullHeal:
                    p.Heal(p.GetMaxHealth() - p.GetHealth(), true);
                    SpellStatus.Fx("vfx_Potion_health_medium", p.transform.position, 3f);
                    return true;
                case On.Cure:
                    if (!p.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectPoison)) return false;
                    p.GetSEMan().RemoveStatusEffect(SEMan.s_statusEffectPoison, true);
                    return true;
                case On.Awaken:
                    if (!SpellStatus.Has(p, SpellStatus.Asleep)) return false;
                    SpellStatus.Take(p, SpellStatus.Asleep);
                    return true;
                case On.Sleep:
                    if (SpellStatus.Has(p, SpellStatus.Asleep) || p.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectPoison)) return false;
                    SpellStatus.Give(p, SpellStatus.Asleep, seconds);
                    return true;
                case On.Poison:
                    if (p.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectPoison) || SpellStatus.Has(p, SpellStatus.Asleep)) return false;
                    var hit = new HitData { m_point = p.GetCenterPoint() };
                    hit.m_damage.m_poison = Ultima.Scale;
                    p.Damage(hit);
                    return true;
                case On.Status:
                    if (amount < 0 || amount >= StatusNames.Length) return false;
                    SpellStatus.Give(p, StatusNames[amount], seconds);
                    return true;
                case On.Wind:
                    // Rel Hur: the wind blows from where it was asked for a while (each player's own sky)
                    EnvMan.instance?.SetDebugWind(amount, 1f);
                    windUntil = Time.time + seconds;
                    return true;
            }
            return false;
        }

        static float windUntil = -1;

        /// <summary>A changed wind settles back to the weather's own in time.</summary>
        public static void Tick()
        {
            if (windUntil > 0 && Time.time > windUntil) { windUntil = -1; EnvMan.instance?.ResetDebugWind(); }
        }

        // ── the spells ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The spell's effect (cast_spell_command's switch), the checks already passed: 1 "Success!",
        /// 0 "Failed!", −1 nothing more to say. <paramref name="fromItem"/>: a scroll's (no
        /// intelligence roll for the daemon, its own times).
        /// </summary>
        public static int Cast(Player me, int spell, bool fromItem = false)
        {
            var at = me.transform.position;
            switch (spell)
            {
                case Magic.InLor:
                case Magic.VasLor:
                {
                    float seconds = Magic.MinutesToSeconds(Magic.LightMinutes(spell, fromItem), DayLength);
                    foreach (var p in Party(me)) Status(p, SpellStatus.Light, seconds);
                    Flash(me, "vfx_pick_wisp");
                    return Succeeded;
                }
                case Magic.GravPor: Missile(me, spell, "DvergerStaffIce_projectile"); return Silent;
                case Magic.VasFlam: Missile(me, spell, "DvergerStaffFire_fireball_projectile"); return Silent;
                case Magic.XenCorp: Missile(me, spell, "charred_fireball_projectile"); return Silent;
                case Magic.AnZu: return Apply(Whom(me), On.Awaken) ? Silent : Failed;
                case Magic.AnNox: return Apply(Whom(me), On.Cure) ? Succeeded : Failed;
                case Magic.Mani: return Apply(Whom(me), On.Heal, Magic.Healing(random)) ? Succeeded : Failed;
                case Magic.VasMani:
                    // as the original: not on the dead, and not while the enemy is still there
                    if (PlaceOf(me) == SpellPlace.Combat) return Failed;
                    return Apply(Whom(me), On.FullHeal) ? Succeeded : Failed;
                case Magic.AnYlem: return Dissolve(me);
                case Magic.AnSanct: return Unlock(me);
                case Magic.AnXenCorp: Frighten(me, undeadOnly: true); return Silent;
                case Magic.InQuasCorp: Frighten(me, undeadOnly: false); return Silent;
                case Magic.RelHur:
                {
                    // the wind blows the way the caster looks (EnvMan's angle: the way it blows to)
                    var f = Facing(me);
                    int angle = Mathf.RoundToInt(Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg + 360) % 360;
                    foreach (var p in Party(me)) Apply(p, On.Wind, angle, DayLength / 4f);
                    Flash(me);
                    return Silent;
                }
                case Magic.InWis: return Locate(me);
                case Magic.KalXen: return Summon(me, Magic.SummonedAnimal(random.Next(16)), 1, allied: true) ? Succeeded : Failed;
                case Magic.InBetXen: return Summon(me, Magic.InsectSwarm, 4, allied: true) ? Succeeded : Failed;
                case Magic.KalXenCorp:
                {
                    // the daemon serves when the caster's intelligence beats a 1..30 roll (a scroll's always does)
                    bool serves = fromItem || CasterIntelligence(me) > random.Next(1, 31);
                    if (!Summon(me, Magic.Daemon, 1, serves)) return Failed;
                    if (!serves) { Say(SOops); return Silent; }
                    return Succeeded;
                }
                case Magic.InXenMani:
                {
                    var food = UltimaItems.Map.Special("Food");
                    var prefab = food != null ? ObjectDB.instance?.GetItemPrefab(food.Prefab) : null;
                    if (prefab == null) return Failed;
                    int n = Magic.CreatedFood(random);
                    if (!me.GetInventory().AddItem(prefab, n)) ItemDrop.DropItem(prefab.GetComponent<ItemDrop>().m_itemData, n, at + Vector3.up, Quaternion.identity);
                    Flash(me, "vfx_lootspawn");
                    return Succeeded;
                }
                case Magic.InFlamGrav:
                case Magic.InNoxGrav:
                case Magic.InZuGrav:
                case Magic.InSanctGrav:
                    SpellField.Lay(me, spell, AimPoint(me, 20f));
                    return Silent;
                case Magic.AnGrav: return SpellField.Dispel(AimPoint(me, 20f), 6f) ? Succeeded : Failed;
                case Magic.InPor: return Blink(me);
                case Magic.InSanct:
                case Magic.RelTym:
                case Magic.InAn:
                {
                    string name = spell == Magic.InSanct ? SpellStatus.Protection : spell == Magic.RelTym ? SpellStatus.Quickness : SpellStatus.NegateMagic;
                    foreach (var p in Party(me)) Status(p, name, Turns(Magic.StatusTurns(spell, fromItem)));
                    Flash(me, spell == Magic.InSanct ? "vfx_StaffShield" : spell == Magic.RelTym ? "vfx_MeadHasty" : "vfx_Smoked");
                    return Succeeded;
                }
                case Magic.QuasAnWis:
                    foreach (var c in Enemies(at, CombatRange).ToList()) SpellStatus.Bewitch(c, SpellStatus.Creature.Confuse, Turns(Magic.StatusTurns(spell, false)));
                    Flash(me, "vfx_MeadTamer");
                    return Succeeded;
                case Magic.UusPor:
                case Magic.DesPor:
                    if (DungeonLadder == null) { Say(SNotHere); return Silent; }
                    return DungeonLadder(me, spell == Magic.UusPor ? -1 : 1) ? Succeeded : Failed;
                case Magic.WisQuas:
                    // nothing in Britannia hides by magic yet: the reveal finds nobody
                    Flash(me);
                    return Silent;
                case Magic.AnExPor: return Seal(me, true);
                case Magic.InExPor: return Seal(me, false);
                case Magic.InZu:
                case Magic.InNoxHur:
                case Magic.InVasGravCorp:
                case Magic.InFlamHur:
                    Wind(me, Magic.WindOf(spell).Value);
                    return Silent;
                case Magic.InVasPorYlem: Earthquake(me); return Silent;
                case Magic.WisAnYlem:
                case Magic.InQuasWis:
                    // the gem's view (In Quas Wis) and x-ray (Wis An Ylem): the map, and a moment's light
                    if (spell == Magic.WisAnYlem) Status(me, SpellStatus.Light, 20f);
                    else Minimap.instance?.SetMapMode(Minimap.MapMode.Large);
                    Flash(me);
                    return Silent;
                case Magic.AnXenEx: return Charm(me);
                case Magic.RelXenBet: return Shrink(me);
                case Magic.SanctLor:
                    Status(me, SpellStatus.Invisible, Turns(FightTurns));
                    Flash(me, "vfx_odin_despawn");
                    return Succeeded;
                case Magic.InQuasXen: return Clone(me);
                case Magic.InManiCorp:
                    // nobody lies dead in Valheim: the fallen are already back at the throne room
                    if (fromItem) Say(SNotDead);
                    return Failed;
                case Magic.VasRelPor:
                    if (GateTravel == null) { Say(SNotHere); return Failed; }
                    // ("To phase: " is asked by the hook: the answer comes later)
                    return GateTravel(me, -1) ? Silent : Failed;
                case Magic.AnTym: return StopTime(me, fromItem);
            }
            return Failed;
        }

        // ── missiles, winds, the earthquake ──────────────────────────────────────────────────

        /// <summary>Grav Por, Vas Flam, Xen Corp: a bolt where the caster looks, hitting for 1..its attack (DATA.OVL item 0x30..0x32).</summary>
        static void Missile(Player me, int spell, string prefab)
        {
            var eye = View(me);
            var target = Aimed(me, AimRange, enemiesOnly: true);
            var dir = target != null ? (target.GetCenterPoint() - me.GetCenterPoint()).normalized : eye.forward;
            var proto = ZNetScene.instance?.GetPrefab(prefab) ?? ZNetScene.instance?.GetPrefab("DvergerStaffFire_fireball_projectile");
            int roll = Ultima.WeaponRoll(Ovl.ItemAttack(Magic.MissileWeapon(spell)), random);
            if (proto == null || proto.GetComponent<Projectile>() == null)
            {
                // no bolt to show: it strikes what is aimed at
                if (target != null) Strike(me, target, roll, true);
                return;
            }
            var from = me.GetCenterPoint() + dir * 0.9f + Vector3.up * 0.3f;
            var go = Object.Instantiate(proto, from, Quaternion.LookRotation(dir));
            var pr = go.GetComponent<Projectile>();
            // its own hit only: no Valheim blast or fire left behind
            pr.m_spawnOnHit = null;
            pr.m_randomSpawnOnHit?.Clear();
            pr.m_aoe = 0f;
            pr.m_gravity = 0f;
            pr.m_ttl = 3f;
            pr.Setup(me, dir * 35f, 10f, SpellHit(me, null, roll, true), null, null);
        }

        /// <summary>The winds (cast_wind_spell): every creature in a cone the way the caster looks, each once.</summary>
        static void Wind(Player me, Magic.WindKind kind)
        {
            var f = Facing(me);
            string fx = kind == Magic.WindKind.Fire ? "fx_DvergerMage_Fire_hit" : kind == Magic.WindKind.Poison ? "vfx_BombBlob_explode_poison"
                : kind == Magic.WindKind.Sleep ? "vfx_Wet" : "vfx_ghost_death";
            for (int i = 1; i <= 4; i++) SpellStatus.Fx(fx, me.transform.position + f * (WindRange * i / 4f) + Vector3.up, 3f);
            foreach (var c in Creatures(me.transform.position, WindRange).ToList())
            {
                var to = c.transform.position - me.transform.position;
                if (Vector3.Angle(f, Vector3.ProjectOnPlane(to, Vector3.up)) > WindHalfAngle) continue;
                switch (kind)
                {
                    case Magic.WindKind.Sleep:
                        if (Evades(me, c) || Immune(c)) continue;
                        SpellStatus.Bewitch(c, SpellStatus.Creature.Sleep, Turns(FightTurns));
                        break;
                    case Magic.WindKind.Poison:
                        if (random.Next(1, 31) < (UltimaCreatures.Of(c)?.Dexterity ?? 0)) continue;
                        Strike(me, c, 0, false, poison: true);
                        break;
                    case Magic.WindKind.Fire:
                        Strike(me, c, random.Next(0, 31), false);
                        break;
                    case Magic.WindKind.Death:
                        if (Evades(me, c) || Immune(c)) continue;
                        Strike(me, c, 99, false);
                        break;
                }
            }
        }

        /// <summary>In Vas Por Ylem: the ground shakes; each enemy whose dexterity is no more than a 1..30 roll takes 1..20 and staggers.</summary>
        static void Earthquake(Player me)
        {
            GameCamera.instance?.AddShake(me.transform.position, 40f, 3f, false);
            SpellStatus.Fx("fx_eikthyr_stomp", me.transform.position, 4f);
            foreach (var c in Enemies(me.transform.position, CombatRange).ToList())
            {
                int dmg = Magic.Earthquake(UltimaCreatures.Of(c)?.Dexterity ?? 0, random);
                if (dmg > 0) Strike(me, c, dmg, false, stagger: true);
            }
        }

        // ── creatures ───────────────────────────────────────────────────────────────────────

        /// <summary>An Xen Corp (the undead) and In Quas Corp (every monster): those who fail to evade flee.</summary>
        static void Frighten(Player me, bool undeadOnly)
        {
            Flash(me, "vfx_ghost_spawn");
            foreach (var c in Enemies(me.transform.position, CombatRange).ToList())
            {
                if (Immune(c) || (undeadOnly && !(UltimaCreatures.Of(c)?.Undead ?? false)) || Evades(me, c)) continue;
                SpellStatus.Bewitch(c, SpellStatus.Creature.Fear, Turns(FightTurns));
            }
        }

        /// <summary>An Xen Ex: the creature under the crosshair fights for the caster (unless immune or it evades).</summary>
        static int Charm(Player me)
        {
            var c = Aimed(me, AimRange, enemiesOnly: true);
            if (c == null) { Say(SNone); return Silent; }
            if (Immune(c) || Evades(me, c)) return Failed;
            SpellStatus.Bewitch(c, SpellStatus.Creature.Charm, Turns(FightTurns));
            SpellStatus.Fx("vfx_MeadTamer", c.transform.position, 3f);
            Say(c.GetHoverName() + S(SCharmed).Replace("!", "") + "!");
            return Silent;
        }

        /// <summary>Rel Xen Bet: the creature under the crosshair becomes a giant rat.</summary>
        static int Shrink(Player me)
        {
            var c = Aimed(me, AimRange);
            if (c == null) { Say(SNone); return Silent; }
            if (Immune(c) || Evades(me, c)) return Failed;
            var at = c.transform.position;
            Vanish(c);
            SpellStatus.Fx("vfx_spawn_small", at, 3f);
            return UltimaCreatures.Spawn(Magic.GiantRat, at, wild: true) != null ? Succeeded : Failed;
        }

        /// <summary>In Quas Xen: a copy of the creature under the crosshair, on its side, appears nearby.</summary>
        static int Clone(Player me)
        {
            var c = Aimed(me, AimRange);
            var stats = c != null ? UltimaCreatures.Of(c) : null;
            if (stats == null) return Failed;
            var at = Near(c.transform.position, 3f, 6f);
            var copy = UltimaCreatures.Spawn(stats.Type, at, wild: true);
            if (copy == null) return Failed;
            SpellStatus.Fx("vfx_spawn", at, 3f);
            if (c.IsTamed()) SpellStatus.Bewitch(copy, SpellStatus.Creature.Charm, Turns(FightTurns));
            return Succeeded;
        }

        /// <summary>Kal Xen, In Bet Xen, Kal Xen Corp: creatures appear around the caster, serving them (or not: the daemon's "Oops...").</summary>
        static bool Summon(Player me, int type, int count, bool allied)
        {
            int made = 0;
            for (int i = 0; i < count; i++)
            {
                var at = Near(me.transform.position, 3f, 7f);
                var c = UltimaCreatures.Spawn(type, at, wild: true);
                if (c == null) break;
                SpellStatus.Fx("vfx_spawn", at, 3f);
                if (allied) SpellStatus.Bewitch(c, SpellStatus.Creature.Summoned, Turns(SummonTurns));
                made++;
            }
            return made > 0;
        }

        /// <summary>An Tym: everything near stands still, unless a Shadowlord is there ("Magic absorbed!").</summary>
        static int StopTime(Player me, bool fromItem)
        {
            var at = me.transform.position;
            int loc = LocationAt(at);
            if (fromItem && (loc == Magic.Stonegate || loc == Magic.Doom)) { Say(SNoEffect); return Silent; }
            var near = Creatures(at, 40f).ToList();
            if (near.Any(c => UltimaCreatures.Of(c)?.Type == Magic.Shadowlord)) { Say(SMagicAbsorbed); return Failed; }
            foreach (var c in near) SpellStatus.Bewitch(c, SpellStatus.Creature.Stop, Turns(Magic.StatusTurns(Magic.AnTym, fromItem)));
            SpellStatus.Fx("fx_DvergerMage_Nova_ring", at, 3f);
            return fromItem ? Succeeded : Silent;
        }

        static void Vanish(Character c)
        {
            var nview = c.GetComponent<ZNetView>();
            if (nview == null || nview.GetZDO() == null) return;
            nview.ClaimOwnership();
            ZNetScene.instance.Destroy(c.gameObject);
        }

        static Vector3 Near(Vector3 at, float from, float to)
        {
            float a = (float)(random.NextDouble() * Math.PI * 2), d = from + (float)random.NextDouble() * (to - from);
            var p = at + new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d);
            if (ZoneSystem.instance != null) p.y = ZoneSystem.instance.GetGroundHeight(p) + 0.2f;
            return p;
        }

        // ── the world ───────────────────────────────────────────────────────────────────────

        /// <summary>In Por (cast_in_por_blink): along the way the caster looks, to the last open land within four tiles (not water, not inside a settlement).</summary>
        static int Blink(Player me)
        {
            var shape = Britannia.Active;
            var f = Facing(me);
            var from = me.transform.position;
            Vector3? to = null;
            float tile = shape?.TileMetres ?? 32f;
            for (int i = 1; i <= 4; i++)
            {
                var p = from + f * tile * i;
                float y = ZoneSystem.instance.GetGroundHeight(p);
                if (y < (Britannia.Active != null ? Britannia.SeaLevel + 0.3f : ZoneSystem.instance.m_waterLevel + 0.3f) || LocationAt(p) != 0) continue;
                p.y = y + 0.3f;
                to = p;
            }
            if (to == null) return Failed;
            SpellStatus.Fx("vfx_odin_despawn", from, 3f);
            me.TeleportTo(to.Value, me.transform.rotation, distantTeleport: false);
            SpellStatus.Fx("vfx_odin_despawn", to.Value, 3f);
            return Succeeded;
        }

        /// <summary>In Wis: where the caster stands, in the map's rune letters.</summary>
        static int Locate(Player me)
        {
            var shape = Britannia.Active;
            if (shape == null) return Failed;
            shape.ToTile(me.transform.position.x, me.transform.position.z, out double u, out double v);
            int x = ((int)Math.Floor(u) % 256 + 256) % 256, y = ((int)Math.Floor(v) % 256 + 256) % 256;
            Say(Magic.RunePosition(x, y));
            Flash(me);
            return Silent;
        }

        /// <summary>An Ylem: a rock under the crosshair (a boulder, a stone) dissolves: "POOF!".</summary>
        static int Dissolve(Player me)
        {
            var eye = View(me);
            if (!Physics.Raycast(eye.position, eye.forward, out var hit, TouchRange + 4f)) return Failed;
            var rock = (Component)hit.collider.GetComponentInParent<MineRock5>() ?? (Component)hit.collider.GetComponentInParent<MineRock>()
                       ?? hit.collider.GetComponentInParent<Destructible>();
            if (rock == null || !rock.gameObject.name.ToLowerInvariant().Contains("rock") || rock.GetComponentInParent<U5Fixed>() != null) return Failed;
            var nview = rock.GetComponentInParent<ZNetView>();
            if (nview == null) return Failed;
            SpellStatus.Fx("vfx_RockDestroyed", rock.transform.position, 3f);
            nview.ClaimOwnership();
            ZNetScene.instance.Destroy(nview.gameObject);
            Say(SPoof);
            return Silent;
        }

        /// <summary>An Sanct: a locked door under the crosshair opens to keys no more, or a trapped chest is disarmed.</summary>
        static int Unlock(Player me)
        {
            var eye = View(me);
            if (!Physics.Raycast(eye.position, eye.forward, out var hit, TouchRange)) return Failed;
            var door = hit.collider.GetComponentInParent<Door>();
            var zdo = door?.GetComponent<ZNetView>()?.GetZDO();
            if (zdo != null && door.GetComponent<U5Fixed>() != null && zdo.GetString(Settlements.LockKey) == "key")
            {
                door.GetComponent<ZNetView>().ClaimOwnership();
                zdo.Set(Settlements.LockKey, "");
                return Succeeded;
            }
            var chest = hit.collider.GetComponentInParent<Container>();
            var czdo = chest?.GetComponent<ZNetView>()?.GetZDO();
            if (czdo != null && czdo.GetBool(Treasure.TrapKey))
            {
                chest.GetComponent<ZNetView>().ClaimOwnership();
                czdo.Set(Treasure.TrapKey, false);
                Say(SDisarmed);
                return Succeeded;
            }
            return Failed;
        }

        /// <summary>An Ex Por seals a door under the crosshair by magic; In Ex Por unseals a magically sealed one.</summary>
        static int Seal(Player me, bool seal)
        {
            var eye = View(me);
            if (!Physics.Raycast(eye.position, eye.forward, out var hit, TouchRange)) return Failed;
            var door = hit.collider.GetComponentInParent<Door>();
            var zdo = door?.GetComponent<ZNetView>()?.GetZDO();
            if (zdo == null || door.GetComponent<U5Fixed>() == null || door.GetComponent<Portcullis>() != null) return Failed;
            string lockKind = zdo.GetString(Settlements.LockKey);
            if (seal ? lockKind == "magic" : lockKind != "magic") return Failed;
            door.GetComponent<ZNetView>().ClaimOwnership();
            zdo.Set(Settlements.LockKey, seal ? "magic" : "");
            if (seal) zdo.Set(ZDOVars.s_state, 0);
            Flash(me);
            return Succeeded;
        }

        // ── scrolls and potions ──────────────────────────────────────────────────────────────

        /// <summary>A scroll read (use_scroll): its spell without mana or reagents, by the scroll's own rules.</summary>
        public static void ReadScroll(Player me, int spell)
        {
            Say(SScroll);
            var place = PlaceOf(me);
            int result;
            switch (spell)
            {
                case Magic.VasLor: Say(SLight); result = Cast(me, Magic.VasLor, true); break;
                case Magic.RelHur:
                    Say(SWind);
                    result = place == SpellPlace.Dungeon ? Failed : Cast(me, Magic.RelHur, true);
                    break;
                case Magic.InSanct: Say(SProtection); result = Cast(me, Magic.InSanct, true); break;
                case Magic.InAn: Say(SNegateMagic); result = Cast(me, Magic.InAn, true); break;
                case Magic.InQuasWis:
                    Say(SView);
                    if (place == SpellPlace.Combat) { Say(SNotHere); result = Silent; }
                    else result = Cast(me, Magic.InQuasWis, true);
                    break;
                case Magic.KalXenCorp:
                    Say(SSummonDaemon);
                    if (place != SpellPlace.Combat) { Say(SNotHere); result = Silent; }
                    else result = Cast(me, Magic.KalXenCorp, true);
                    break;
                case Magic.InManiCorp:
                    Say(SResurrection);
                    if (place == SpellPlace.Combat) { Say(SNotHere); result = Silent; }
                    else result = Cast(me, Magic.InManiCorp, true);
                    break;
                case Magic.AnTym:
                    int loc = LocationAt(me.transform.position);
                    if (loc != Magic.Stonegate && loc != Magic.Doom) Say(SNegateTime);
                    result = Cast(me, Magic.AnTym, true);
                    break;
                default: result = Cast(me, spell, true); break;
            }
            if (result == Failed) Say(SFailed);
        }

        /// <summary>
        /// A potion drunk (use_potion): by the drinker, whatever its colour says one time in
        /// eight. Blue wakes, yellow heals, red cures, green poisons, orange sleeps; in a fight
        /// purple is a rat's shape ("Poof!") and black invisibility; white is the x-ray view.
        /// </summary>
        public static void Drink(Player me, int potion)
        {
            Say(SPotion);
            bool fight = PlaceOf(me) == SpellPlace.Combat;
            SpellStatus.Fx("vfx_MeadSplash", me.transform.position, 3f);
            switch (Magic.Drunk(potion, random))
            {
                case Magic.Blue: if (!ApplyHere(me, On.Awaken, 0, 0)) Say(SFailed); break;
                case Magic.Yellow: if (ApplyHere(me, On.Heal, Magic.Healing(random), 0)) Say(SHealed); else Say(SFailed); break;
                case Magic.Red: if (ApplyHere(me, On.Cure, 0, 0)) Say(SCured); else Say(SFailed); break;
                case Magic.Green: if (ApplyHere(me, On.Poison, 0, 0)) Say(SPoisoned); else Say(SFailed); break;
                case Magic.Orange: if (ApplyHere(me, On.Sleep, 0, Turns(PlayerSleepTurns))) Say(SSlept); else Say(SFailed); break;
                case Magic.Purple:
                    if (fight) Say(SPotionPoof);
                    else Say(SNoNoticeable);
                    break;
                case Magic.Black:
                    if (fight) { SpellStatus.Give(me, SpellStatus.Invisible, Turns(FightTurns)); Say(SInvisible); }
                    else Say(SNoNoticeable);
                    break;
                case Magic.White:
                    if (PlaceOf(me) == SpellPlace.Town || PlaceOf(me) == SpellPlace.Overworld) SpellStatus.Give(me, SpellStatus.Light, 20f);
                    else Say(SNoNoticeable);
                    break;
            }
        }
    }

    /// <summary>
    /// The field spells (In Flam Grav fire, In Nox Grav poison, In Zu Grav sleep, In Sanct Grav
    /// energy): laid where the caster aims, a field works on the creatures in it each turn and
    /// dissipates as the original's do (16 in 256 a turn: COMSUBS dissipate_fields). The caster's
    /// machine keeps it; every peer sees it.
    /// </summary>
    public class SpellField : MonoBehaviour
    {
        static readonly List<SpellField> all = new List<SpellField>();
        static readonly System.Random random = new System.Random();
        int spell;
        Player caster;
        float next;

        public static void Lay(Player me, int spell, Vector3 at)
        {
            var go = new GameObject("u5_field");
            go.transform.position = at;
            var f = go.AddComponent<SpellField>();
            f.spell = spell;
            f.caster = me;
            f.Turn();
        }

        /// <summary>An Grav: the fields near a point are gone.</summary>
        public static bool Dispel(Vector3 at, float range)
        {
            var near = all.Where(f => f != null && Vector3.Distance(f.transform.position, at) <= range).ToList();
            foreach (var f in near) f.Gone();
            return near.Count > 0;
        }

        void Awake() => all.Add(this);
        void OnDestroy() => all.Remove(this);

        void Update()
        {
            if (Time.time < next) return;
            if (caster == null || random.Next(256) < 16) { Gone(); return; }
            Turn();
        }

        void Gone()
        {
            SpellStatus.Fx("vfx_Smoked", transform.position, 2f);
            Destroy(gameObject);
        }

        string Sight => spell == Magic.InFlamGrav ? "fx_DvergerMage_Fire_hit" : spell == Magic.InNoxGrav ? "vfx_BombBlob_explode_poison"
            : spell == Magic.InZuGrav ? "vfx_Wet" : "DvergerStaffBlocker_blockCircle";

        /// <summary>One turn of the field: its sight, and what it does to the creatures standing in it.</summary>
        void Turn()
        {
            next = Time.time + Magic.TurnSeconds;
            SpellStatus.Fx(Sight, transform.position, Magic.TurnSeconds + 0.5f);
            int attack = Plugin.U5.Ovl.ItemAttack(Magic.MissileWeapon(spell));
            foreach (var c in SpellEffects.Creatures(transform.position, SpellEffects.FieldRadius).ToList())
            {
                if (spell == Magic.InSanctGrav)
                {
                    // the energy field stops whoever is in it
                    SpellStatus.Bewitch(c, SpellStatus.Creature.Stop, Magic.TurnSeconds);
                    continue;
                }
                if (spell == Magic.InZuGrav)
                {
                    var stats = UltimaCreatures.Of(c);
                    if (!Magic.Immune(stats?.Type ?? -1) && !Magic.Evades(Avatar.Load(caster.m_customData).Intelligence, stats?.Skill ?? 0, random.Next(1, 31)))
                        SpellStatus.Bewitch(c, SpellStatus.Creature.Sleep, SpellEffects.FightTurns * Magic.TurnSeconds);
                    continue;
                }
                var hit = SpellEffects.SpellHit(caster, c, Ultima.WeaponRoll(Math.Max(1, attack), random), true);
                if (spell == Magic.InNoxGrav) hit.m_damage.m_poison = Ultima.Scale;
                c.Damage(hit);
            }
        }
    }
}
