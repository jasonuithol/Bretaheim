using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Bretaheim.WorldGen.Core;
using UnityEngine;

namespace Bretaheim.WorldGen
{
    /// <summary>What a townsperson is doing about a monster (or a criminal) in town.</summary>
    public enum Duty { None, Messenger, Hide, Recruit, Fight, Leave, Operate, Arrest }

    /// <summary>
    /// A town under attack (per settlement, on the peer that moves its townsfolk). A monster in
    /// the town square is seen by the nearest villager, who runs to the nearest guard with the
    /// news; everyone else runs home and locks the door. The alerted guard fights, or, when the
    /// job looks big (one guard per 300 hp of monster), first runs to fetch more guards. Guards who
    /// see a monster themselves join in. The Britannys without guards (North Britanny) send their
    /// villager running out towards Lord British's castle (town_tiles.json help_from), whose
    /// guards come back with them. After five minutes of fighting, or with every guard down,
    /// reinforcements run in from the town's edge. Five quiet seconds after the last monster the
    /// doors unlock and everyone goes back to their day; reinforcements leave the way they came.
    /// </summary>
    public class TownWatch
    {
        const float ScanEvery = 0.5f, Sees = 25f, GuardSees = 20f, Beyond = 6f, Touch = 2.5f, LockReach = 8f;
        public const string AlarmLockKey = "u5_alarm_lock";

        readonly Settlements.Built town;
        readonly List<TownNpc> folk = new List<TownNpc>();
        readonly List<Character> threats = new List<Character>();
        readonly List<ZNetView> lockedDoors = new List<ZNetView>();
        float scanAt, since = -1, quietSince = -1;
        bool reinforced;
        /// <summary>A town without guards: the edge tile the messenger runs out of for help, and who sends it.</summary>
        int helpEdge = -1;
        Settlements.Built helper;
        int needed = 1;

        public TownWatch(Settlements.Built town) { this.town = town; }

        public bool Alarmed => since >= 0;
        public IReadOnlyList<Character> Threats => threats;

        public void Join(TownNpc npc) { if (!folk.Contains(npc)) folk.Add(npc); }
        public void Part(TownNpc npc) => folk.Remove(npc);

        /// <summary>Called by each owned townsperson every frame; runs once per scan.</summary>
        public void Tick()
        {
            if (Time.time < scanAt) return;
            scanAt = Time.time + ScanEvery;
            folk.RemoveAll(f => f == null);
            var mine = folk.Where(f => f.IsOwned).ToList();
            if (mine.Count == 0) return;
            Scan();
            Arrests(mine);
            if (threats.Count == 0)
            {
                if (Alarmed)
                {
                    if (quietSince < 0) quietSince = Time.time;
                    else if (Time.time - quietSince >= TownAlarm.QuietFor) End(mine);
                }
                return;
            }
            quietSince = -1;
            var guards = mine.Where(f => f.IsGuard && !f.IsDead).ToList();
            int allGuards = mine.Count(f => f.IsGuard && !f.IsReinforcement);

            // guards who see a monster themselves join in
            foreach (var g in guards)
                if (g.Duty == Duty.None && g.Floor == 0 && threats.Any(t => Vector3.Distance(t.transform.position, g.transform.position) < GuardSees))
                    g.Duty = Duty.Fight;

            if (!Alarmed)
            {
                // the first to see it runs for a guard; everyone else runs home
                var villagers = mine.Where(f => !f.IsGuard && !f.IsDead).ToList();
                // (prisoners can't get out of their cells to tell anyone)
                var spotter = villagers.Where(v => !v.IsPrisoner && threats.Any(t => Vector3.Distance(t.transform.position, v.transform.position) < Sees))
                    .OrderBy(v => threats.Min(t => Vector3.Distance(t.transform.position, v.transform.position))).FirstOrDefault();
                if (spotter == null && !guards.Any(g => g.Duty == Duty.Fight)) return; // nobody has seen it yet
                since = Time.time;
                reinforced = false;
                needed = TownAlarm.GuardsNeeded(threats.Sum(t => t.GetMaxHealth()), allGuards);
                Plugin.Log.LogInfo($"{town.Location.Name}: alarm! {threats.Count} monster(s), {needed} guard(s) needed{(spotter != null ? $"; {spotter.GetHoverName()} saw it" : "")}");
                helpEdge = -1;
                helper = null;
                if (guards.Count == 0 && allGuards == 0 && spotter != null)
                {
                    // no guards here: run to where the town sends for help (town_tiles.json help_from: the Britannys, the castle)
                    helper = Settlements.Tiles != null && Settlements.Tiles.HelpFrom.TryGetValue(town.Location.Number, out int from) ? Settlements.Get(from) : null;
                    if (helper != null && TownsfolkSpawner.GuardOf(helper.Location) == null) helper = null;
                    if (helper != null)
                    {
                        var dir = (helper.Origin - town.Origin).normalized * town.Site.Half;
                        helpEdge = TownAlarm.EdgeNear(town.Plan, dir.x, dir.z);
                        Plugin.Log.LogInfo($"{town.Location.Name} has no guards: {spotter.GetHoverName()} runs to {helper.Location.Name} for help");
                    }
                }
                foreach (var v in villagers) v.Duty = v == spotter && (guards.Count > 0 || helpEdge >= 0) ? Duty.Messenger : Duty.Hide;
            }
            else
                foreach (var v in mine.Where(f => !f.IsGuard && f.Duty == Duty.None && !f.IsDead)) v.Duty = Duty.Hide;

            // the messenger reaches a guard: the news is out
            foreach (var m in mine.Where(f => f.Duty == Duty.Messenger))
            {
                if (helpEdge >= 0)
                {
                    // out of town for help: the helpers' guards come back in with them
                    if (!m.ArrivedAt(helpEdge)) { m.RunTo(0, helpEdge); continue; }
                    reinforced = true;
                    int come = Math.Max(2, needed);
                    TownsfolkSpawner.SpawnReinforcements(town, helpEdge, come, helper);
                    Plugin.Log.LogInfo($"{town.Location.Name}: {come} guards of {helper.Location.Name} come back with {m.GetHoverName()}");
                    helpEdge = -1;
                    m.Duty = Duty.Hide;
                    continue;
                }
                var guard = NearestGuard(guards, m.transform.position, g => g.Duty != Duty.Fight) ?? NearestGuard(guards, m.transform.position, g => true);
                if (guard == null) { m.Duty = Duty.Hide; continue; }
                if (Vector3.Distance(guard.transform.position, m.transform.position) < Touch)
                {
                    Alert(guard, guards);
                    m.Duty = Duty.Hide;
                }
                else m.RunTo(guard);
            }

            // a guard fetching others: each one reached joins the fight; enough, and they go too
            foreach (var r in guards.Where(g => g.Duty == Duty.Recruit).ToList())
            {
                var idle = NearestGuard(guards, r.transform.position, g => g.Duty == Duty.None);
                if (idle == null || guards.Count(g => g.Duty == Duty.Fight) + 1 >= needed) { r.Duty = Duty.Fight; continue; }
                if (Vector3.Distance(idle.transform.position, r.transform.position) < Touch) idle.Duty = Duty.Fight;
                else r.RunTo(idle);
            }

            // hidden at home: the door is locked
            foreach (var h in mine.Where(f => f.Duty == Duty.Hide && f.Hidden)) LockNearestDoor(h.transform.position);


            int standing = guards.Count(g => !g.IsReinforcement);
            if (!reinforced && TownAlarm.ReinforcementsDue(Time.time - since, standing, allGuards))
            {
                reinforced = true;
                var t = threats[0].transform.position - town.Origin;
                int edge = TownAlarm.EdgeNear(town.Plan, t.x, t.z);
                int count = Math.Max(2, needed);
                if (edge >= 0) TownsfolkSpawner.SpawnReinforcements(town, edge, count);
                Plugin.Log.LogInfo($"{town.Location.Name}: {count} reinforcements arrive after {Time.time - since:0} s ({standing} guard(s) standing)");
            }
        }

        /// <summary>The fight's target for a guard: the nearest monster in town.</summary>
        public Character Target(Vector3 from) => threats.Where(t => t != null && !t.IsDead()).OrderBy(t => Vector3.Distance(t.transform.position, from)).FirstOrDefault();

        void Alert(TownNpc guard, List<TownNpc> guards)
        {
            int fighting = guards.Count(g => g.Duty == Duty.Fight);
            guard.Duty = fighting + 1 >= needed ? Duty.Fight : Duty.Recruit;
            Plugin.Log.LogInfo($"{town.Location.Name}: the guard is told; {(guard.Duty == Duty.Fight ? "they go to fight" : $"they fetch {needed - fighting - 1} more")}");
        }

        static TownNpc NearestGuard(List<TownNpc> guards, Vector3 from, Func<TownNpc, bool> which)
            => guards.Where(which).OrderBy(g => Vector3.Distance(g.transform.position, from)).FirstOrDefault();

        void Scan()
        {
            threats.Clear();
            var someone = Player.m_localPlayer ?? (Player.GetAllPlayers().Count > 0 ? Player.GetAllPlayers()[0] : null);
            foreach (var c in Character.GetAllCharacters())
            {
                // (the settlement's own creatures live there: no alarm)
                if (c == null || c.IsPlayer() || c.IsDead() || c.IsTamed() || c.GetBaseAI() == null || ResidentMonster.Is(c)) continue;
                if (someone != null ? !BaseAI.IsEnemy(c, someone) : c.GetFaction() == Character.Faction.Players) continue;
                if (!InTown(c.transform.position)) continue;
                threats.Add(c);
            }
            // a player who would not come quietly is fought like a monster (raise_alarm)
            wanted.Clear();
            foreach (var p in Player.GetAllPlayers())
            {
                if (p == null || p.IsDead() || !InTown(p.transform.position)) continue;
                if (Crime.HostileTo(p) == town.Location.Number) threats.Add(p);
                else if (Crime.WantedBy(p) == town.Location.Number) wanted.Add(p);
            }
        }

        bool InTown(Vector3 at)
        {
            var local = at - town.Origin;
            return Mathf.Max(Mathf.Abs(local.x), Mathf.Abs(local.z)) <= town.Site.Half + Beyond && local.y >= -town.Plan.BasementDepth - 2 && local.y <= 3f * town.Plan.Storey;
        }

        readonly List<Player> wanted = new List<Player>();

        /// <summary>The nearest player in town the guards want to arrest.</summary>
        public Player WantedNear(Vector3 from) => wanted.Where(p => p != null).OrderBy(p => Vector3.Distance(p.transform.position, from)).FirstOrDefault();

        /// <summary>
        /// Two guards go for each wanted player; with nobody wanted they go back to their day,
        /// unless someone would not come quietly: then the guards who came attack at once (raise_alarm).
        /// </summary>
        void Arrests(List<TownNpc> mine)
        {
            if (wanted.Count == 0)
            {
                bool rogue = threats.Any(t => t is Player);
                foreach (var g in mine.Where(g => g.IsGuard && !g.IsDead && g.Duty == Duty.Arrest)) g.Duty = rogue ? Duty.Fight : Duty.None;
                return;
            }
            var guards = mine.Where(f => f.IsGuard && !f.IsDead && !f.IsReinforcement).ToList();
            int going = guards.Count(g => g.Duty == Duty.Arrest);
            foreach (var p in wanted)
                foreach (var g in guards.Where(g => g.Duty == Duty.None).OrderBy(g => Vector3.Distance(g.transform.position, p.transform.position)).Take(Math.Max(0, 2 - going)).ToList())
                {
                    g.Duty = Duty.Arrest;
                    going++;
                    Plugin.Log.LogInfo($"{town.Location.Name}: a guard goes to arrest {p.GetPlayerName()}");
                }
        }

        void LockNearestDoor(Vector3 at)
        {
            Door nearest = null;
            float best = LockReach;
            foreach (var door in UnityEngine.Object.FindObjectsByType<Door>(FindObjectsSortMode.None))
            {
                float d = Vector3.Distance(door.transform.position, at);
                if (d < best) { best = d; nearest = door; }
            }
            var nview = nearest != null ? nearest.GetComponent<ZNetView>() : null;
            if (nview == null || nview.GetZDO() == null || nview.GetZDO().GetBool(AlarmLockKey)) return;
            nview.ClaimOwnership();
            nview.GetZDO().Set(ZDOVars.s_state, 0);
            nview.GetZDO().Set(AlarmLockKey, true);
            lockedDoors.Add(nview);
        }

        void End(List<TownNpc> mine)
        {
            Plugin.Log.LogInfo($"{town.Location.Name}: the fight is over after {Time.time - since:0} s");
            since = -1;
            quietSince = -1;
            foreach (var f in mine)
            {
                if (f.Duty == Duty.Operate) continue; // (the ballista dismisses its crew)
                f.Duty = f.IsReinforcement ? Duty.Leave : Duty.None;
                // the standing guards are whole again for the next fight
                if (f.IsGuard && !f.IsDead) f.Heal();
            }
            foreach (var door in lockedDoors)
                if (door != null && door.GetZDO() != null)
                {
                    door.ClaimOwnership();
                    door.GetZDO().Set(AlarmLockKey, false);
                }
            lockedDoors.Clear();
        }

        // ── doors locked against the monsters ───────────────────────────────────────────────

        public static void Install(Harmony harmony)
        {
            var interact = AccessTools.Method(typeof(Door), "Interact");
            if (interact != null) harmony.Patch(interact, prefix: new HarmonyMethod(typeof(TownWatch), nameof(DoorInteract_Prefix)));
        }

        static bool DoorInteract_Prefix(Door __instance, Humanoid character, ref bool __result)
        {
            var zdo = __instance.GetComponent<ZNetView>()?.GetZDO();
            // (townsfolk have the keys to their own doors)
            if (zdo == null || !zdo.GetBool(AlarmLockKey) || (character != null && character.GetComponent<TownNpc>() != null)) return true;
            character?.Message(MessageHud.MessageType.Center, "Locked! The townsfolk hide from the monsters.");
            __result = false;
            return false;
        }
    }
}
