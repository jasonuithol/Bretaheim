using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using U5InValheim.Data;
using U5InValheim.WorldGen.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// The shrines of virtue and the Shrine of the Codex (MASTER_PLAN §5.9 karma; Data/Shrines):
    /// each built on its surface square from its 11×11 MISCMAPS.DAT map (2 m a tile: stone walls,
    /// rock piles, the hot brazier that is the altar; the Codex's walled court, pillars, torches
    /// and the blue-burning Codex), with a stone before the altar to kneel at (E: Meditate). The
    /// virtue, then three mantras, are typed; the original's quests, offerings and rewards follow.
    /// The Codex reads the quest it was sought for. A shrine the Shadowlords have ruined
    /// (<see cref="Desecrate"/>, a world key) burns no more until the word of power of its
    /// dungeon is yelled before it and the virtue and mantras are spoken (meditate_at_shrine).
    /// The Shrine of Spirituality is on no map: <see cref="Visit"/> is for the moongate that leads there.
    /// </summary>
    public static class Shrines
    {
        public const string AltarName = "u5_shrine_altar", ShrineKey = "u5_shrine", RuinedKey = "u5_shrine_ruined_";
        public const int Codex = 8;
        const float Tile = 2f;
        static ShrineData data;
        public static ShrineData Data => data ?? (data = ShrineData.Available(Plugin.U5.Directory) ? ShrineData.Load(Plugin.U5.Directory, Plugin.U5.Ovl) : null);
        static Type clearAreaType;

        static bool Active => Britannia.Active != null;

        public static void Install(Harmony harmony)
        {
            PrefabManager.OnVanillaPrefabsAvailable += Create;
            clearAreaType = AccessTools.Inner(typeof(ZoneSystem), "ClearArea");
            var place = AccessTools.Method(typeof(ZoneSystem), "PlaceLocations");
            if (place != null && clearAreaType != null) harmony.Patch(place, postfix: new HarmonyMethod(typeof(Shrines), nameof(PlaceLocations_Postfix)));
            else Plugin.Log.LogWarning("Shrines: ZoneSystem.PlaceLocations not found");
        }

        /// <summary>The stone to kneel at: a low block of stone, with the shrine's interaction.</summary>
        static void Create()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Create;
            var prefab = PrefabManager.Instance.CreateClonedPrefab(AltarName, "stone_wall_1x1");
            if (prefab == null) { Plugin.Log.LogError("Shrines: no stone_wall_1x1"); return; }
            foreach (var c in prefab.GetComponents<WearNTear>()) Object.DestroyImmediate(c);
            foreach (var c in prefab.GetComponents<Piece>()) Object.DestroyImmediate(c);
            prefab.transform.localScale = new Vector3(1.2f, 0.5f, 0.7f);
            prefab.AddComponent<ShrineAltar>();
            PrefabManager.Instance.AddPrefab(new CustomPrefab(prefab, false));
        }

        // ── ruins (the Shadowlords; Blackthorn's interrogation gives up a mantra) ────────────

        public static bool IsRuined(int shrine) => shrine >= 0 && shrine < ShrineData.Count && ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(RuinedKey + shrine);

        /// <summary>The shrine of a virtue is ruined (BLCKTHRN: g_shrine_destroyed[virtue] = 0xff). For the Blackthorn/Shadowlord work.</summary>
        public static void Desecrate(int shrine)
        {
            if (shrine < 0 || shrine >= ShrineData.Count || ZoneSystem.instance == null) return;
            ZoneSystem.instance.SetGlobalKey(RuinedKey + shrine);
            Plugin.Log.LogInfo($"Shrines: the shrine of {Data?.Virtues[shrine]} is ruined");
        }

        public static void Restore(int shrine) => ZoneSystem.instance?.RemoveGlobalKey(RuinedKey + shrine);

        // ── building ────────────────────────────────────────────────────────────────────────

        static List<(Poi poi, int shrine)> sites;

        static List<(Poi, int)> Sites()
        {
            if (sites != null) return sites;
            sites = new List<(Poi, int)>();
            var d = Data;
            if (d == null) return sites;
            foreach (var p in Britannia.Pois)
            {
                if (p.Underworld) continue;
                if (p.Kind == "codex") sites.Add((p, Codex));
                else if (p.Kind == "shrine" && d.At(p.TileX, p.TileY) >= 0) sites.Add((p, d.At(p.TileX, p.TileY)));
            }
            return sites;
        }

        /// <summary>Called when a new world is activated.</summary>
        public static void Reset() => sites = null;

        /// <summary>What stands on a tile of a shrine's map: (prefab, height above the ground, scale y, stacked courses).</summary>
        static IEnumerable<(string prefab, float y, Vector3 scale)> Pieces(byte tile, bool codex)
        {
            switch (tile)
            {
                case 0x4d: // a stone wall (the shrine's broken ring)
                case 0x4f: // a wall (the Codex's court)
                    int courses = tile == 0x4f ? 3 : 2;
                    for (int k = 0; k < courses; k++) yield return ("stone_wall_2x1", 0.5f + k, Vector3.one);
                    break;
                case 0x4c: yield return ("stone_pile", 0f, Vector3.one * 1.3f); break;           // a pile of rocks
                case 0x46: yield return ("stone_pillar", 2f, new Vector3(1, 2, 1)); break;       // a pillar
                case 0xb0: case 0xb1: yield return ("CastleKit_groundtorch", 0f, Vector3.one); break; // a flickering torch
                case 0xb2: yield return ("piece_brazierfloor01", 1f, Vector3.one); break;         // a hot brazier: the altar
                case 0x41: yield return ("MountainKit_brazier_blue", 0f, Vector3.one * 1.5f); break; // the Codex
            }
        }

        static void PlaceLocations_Postfix(Vector2s zoneID, IList clearAreas, ZoneSystem.SpawnMode mode, List<GameObject> spawnedObjects)
        {
            if (!Active || (mode != ZoneSystem.SpawnMode.Full && mode != ZoneSystem.SpawnMode.Ghost)) return;
            var d = Data;
            if (d == null) return;
            foreach (var (poi, shrine) in Sites())
            {
                var centre = Britannia.TileCentre(poi.TileX, poi.TileY);
                if (ZoneSystem.GetZone(centre) != zoneID) continue;
                clearAreas.Add(Activator.CreateInstance(clearAreaType, centre, 14f));
                bool codex = shrine == Codex;
                int made = 0;
                for (int ty = 0; ty < ShrineData.MapSize; ty++)
                for (int tx = 0; tx < ShrineData.MapSize; tx++)
                {
                    byte tile = d.MapTile(codex, tx, ty);
                    var at = Local(centre, tx, ty);
                    // walls run along their neighbours
                    bool alongX = tx > 0 && d.MapTile(codex, tx - 1, ty) == tile || tx < 10 && d.MapTile(codex, tx + 1, ty) == tile;
                    float yaw = tile == 0x4d || tile == 0x4f ? (alongX ? 0 : 90) : 0;
                    foreach (var (prefab, y, scale) in Pieces(tile, codex))
                        if (Spawn(prefab, at + Vector3.up * y, Quaternion.Euler(0, yaw, 0), scale, mode, spawnedObjects, null) != null) made++;
                }
                // the stone to kneel at: before the altar (where the original's Avatar kneels), or before the Codex
                var kneel = codex ? Local(centre, 5, 3) : Local(centre, 5, 6);
                Spawn(AltarName, kneel + Vector3.up * 0.25f, Quaternion.identity, Vector3.one, mode, spawnedObjects, zdo => zdo.Set(ShrineKey, shrine));
                Plugin.Log.LogInfo($"Shrines: {poi.Name} built at {centre} ({made} pieces)");
            }
        }

        /// <summary>A tile of a shrine's map in the world (row 10 to the south), on the ground there.</summary>
        static Vector3 Local(Vector3 centre, int tx, int ty)
        {
            float x = centre.x + (tx - 5) * Tile, z = centre.z + (5 - ty) * Tile;
            return new Vector3(x, Britannia.SeaLevel + Mathf.Max(0f, Britannia.Active.Height(x, z)), z);
        }

        static GameObject Spawn(string prefabName, Vector3 at, Quaternion rot, Vector3 scale, ZoneSystem.SpawnMode mode, List<GameObject> spawned, Action<ZDO> set)
        {
            var prefab = ZNetScene.instance.GetPrefab(prefabName);
            if (prefab == null) { Plugin.Log.LogWarning($"Shrines: no prefab {prefabName}"); return null; }
            if (mode == ZoneSystem.SpawnMode.Ghost) ZNetView.StartGhostInit();
            var go = Object.Instantiate(prefab, at, rot);
            if (scale != Vector3.one) go.transform.localScale = scale;
            var zdo = go.GetComponent<ZNetView>()?.GetZDO();
            if (zdo != null)
            {
                zdo.Set(Settlements.FixedKey, true);
                if (scale != Vector3.one) zdo.Set(Settlements.ScaleKey, scale);
                set?.Invoke(zdo);
                var wnt = go.GetComponent<WearNTear>();
                if (wnt != null) FixedPieces.Apply(wnt, zdo);
            }
            if (mode == ZoneSystem.SpawnMode.Ghost)
            {
                spawned.Add(go);
                ZNetView.FinishGhostInit();
            }
            return go;
        }

        // ── the visit ───────────────────────────────────────────────────────────────────────

        /// <summary>Visiting a shrine (also the moongate's way to Spirituality): kneel and meditate, or the Codex.</summary>
        public static void Visit(Player me, int shrine, ShrineAltar at) => at?.Begin(me, shrine);
    }

    /// <summary>The stone before a shrine's altar, or before the Codex: E to meditate (or read).</summary>
    public class ShrineAltar : MonoBehaviour, Hoverable, Interactable, TextReceiver, ITalker
    {
        enum Step { None, Word, Virtue, Mantra, Offer }

        ZNetView nview;
        int shrine = -1;
        Step step;
        string virtue = "";
        readonly List<string> mantras = new List<string>();
        bool restoring;

        void Awake()
        {
            nview = GetComponent<ZNetView>();
            shrine = nview?.GetZDO()?.GetInt(Shrines.ShrineKey, -1) ?? -1;
        }

        int Index => shrine >= 0 ? shrine : nview?.GetZDO()?.GetInt(Shrines.ShrineKey, -1) ?? -1;
        static ShrineData D => Shrines.Data;
        bool IsCodex => Index == Shrines.Codex;

        public string GetHoverName()
        {
            int i = Index;
            if (D == null || i < 0) return "Shrine";
            if (i == Shrines.Codex) return "The Shrine of the Codex";
            return (Shrines.IsRuined(i) ? "A ruined shrine of " : "The Shrine of ") + D.Virtues[i];
        }
        public float GetHoverOffset() => 0f;
        public string GetHoverText() => Localization.instance.Localize(GetHoverName() + $"\n[<color=yellow><b>$KEY_Use</b></color>] {(IsCodex ? "Read" : Shrines.IsRuined(Index) ? "Yell" : "Meditate")}");

        // ITalker (TalkPanel's answers)
        public int Location => 0;
        public int Slot => 0;
        public TlkScript Script => null;
        public void EndTalk() { }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || user != Player.m_localPlayer || D == null || Index < 0) return false;
            Begin((Player)user, Index);
            return true;
        }

        public void Begin(Player me, int i)
        {
            shrine = i;
            mantras.Clear();
            virtue = "";
            restoring = false;
            if (i == Shrines.Codex) { ReadCodex(me); return; }
            if (Shrines.IsRuined(i))
            {
                // meditate_at_shrine: the word of power of its dungeon, yelled before the ruins
                restoring = true;
                Ask(Step.Word, "Yell:");
                return;
            }
            Say(D.Msg(ShrineData.SApproach) + D.Msg(ShrineData.SKneel), ("Meditate", () => Ask(Step.Virtue, D.Msg(ShrineData.SMeditate))), ("Leave", () => { }));
        }

        void Ask(Step s, string prompt)
        {
            step = s;
            // (asked from SetText, the box would be shut again at once: Enter hides it after handing the answer over)
            StartCoroutine(AskNextFrame(prompt.Replace("\n", " ").Replace(":", "").Trim()));
        }

        System.Collections.IEnumerator AskNextFrame(string prompt)
        {
            yield return null;
            TextInput.instance?.RequestText(this, prompt, 15);
        }

        public string GetText() => "";

        public void SetText(string text)
        {
            var me = Player.m_localPlayer;
            if (me == null || D == null) return;
            text = (text ?? "").Trim();
            var s = step;
            step = Step.None;
            switch (s)
            {
                case Step.Word:
                    if (text.IndexOf(D.Words[shrine], StringComparison.OrdinalIgnoreCase) < 0) { me.Message(MessageHud.MessageType.Center, "No effect!"); return; }
                    me.Message(MessageHud.MessageType.Center, D.Ovl(ShrineData.SWordUttered).Trim());
                    Quake();
                    Ask(Step.Virtue, "Upon what virtue dost thou meditate?");
                    return;
                case Step.Virtue:
                    if (text.Length == 0) return;
                    virtue = text;
                    Ask(Step.Mantra, D.Ovl(ShrineData.SMantra));
                    return;
                case Step.Mantra:
                    if (text.Length == 0) return;
                    mantras.Add(text);
                    if (mantras.Count < 3) { Ask(Step.Mantra, D.Ovl(ShrineData.SMantra)); return; }
                    Meditated(me);
                    return;
                case Step.Offer:
                    Offer(me, text);
                    return;
            }
        }

        void Meditated(Player me)
        {
            var m = mantras.ToArray();
            if (restoring)
            {
                if (ShrineRules.Restores(D, shrine, virtue, m))
                {
                    Shrines.Restore(shrine);
                    Quake();
                    Say(D.Ovl(ShrineData.SRestored), ("Bye", () => { }));
                    Plugin.Log.LogInfo($"Shrines: {me.GetPlayerName()} restored the shrine of {D.Virtues[shrine]}");
                }
                return;
            }
            if (!ShrineRules.Focused(D, shrine, virtue, m)) { Say(D.Msg(ShrineData.SUnfocused), ("Bye", () => { })); return; }
            var data = me.m_customData;
            int quests = Get(data, ShrineRules.QuestsKey), done = Get(data, ShrineRules.DoneKey);
            var outcome = ShrineRules.Meditate(shrine, ref quests, done);
            data[ShrineRules.QuestsKey] = quests.ToString();
            Plugin.Log.LogInfo($"Shrines: {me.GetPlayerName()} at {D.Virtues[shrine]}: {outcome}");
            switch (outcome)
            {
                case ShrineOutcome.Ordained:
                    Say(D.Msg(ShrineData.SOrdained) + D.Msg(ShrineData.SQuest) + D.Quest(shrine) + "\"" + D.Msg(ShrineData.SReturn), ("Bye", () => { }));
                    break;
                case ShrineOutcome.Offering:
                    Ask(Step.Offer, D.Msg(ShrineData.SOffer).Trim() + " (0-9)");
                    break;
                case ShrineOutcome.WellDone:
                    int karma = Karma.Get(data);
                    var a = AvatarStats.Mine;
                    var (s, x, i) = ShrineRules.Reward(D, shrine, ref karma, a);
                    a.Save(data);
                    KarmaDeeds.Change(me, karma - Karma.Get(data), "the shrine");
                    Quake();
                    Say(D.Msg(ShrineData.SWellDone) + (s ? D.Ovl(ShrineData.SStrength) : "") + (x ? D.Ovl(ShrineData.SDexterity) : "") + (i ? D.Ovl(ShrineData.SIntelligence) : ""), ("Bye", () => { }));
                    break;
            }
        }

        void Offer(Player me, string text)
        {
            if (text.Length == 0 || !char.IsDigit(text[0]) || text[0] == '0') { me.Message(MessageHud.MessageType.Center, "0 gp"); return; }
            int hundreds = text[0] - '0';
            int gold = me.GetInventory().CountItems("$item_coins");
            int karma = Karma.Get(me.m_customData);
            if (!ShrineRules.Offer(hundreds, gold, ref karma))
            {
                me.Message(MessageHud.MessageType.Center, D.Msg(ShrineData.SNoGold));
                Ask(Step.Offer, D.Msg(ShrineData.SOffer).Trim() + " (0-9)");
                return;
            }
            me.GetInventory().RemoveItem("$item_coins", hundreds * 100);
            KarmaDeeds.Change(me, karma - Karma.Get(me.m_customData), $"an offering of {hundreds}00 gp");
            Say(D.Ovl(ShrineData.SAlakazam) + "!", ("Bye", () => { }));
        }

        /// <summary>The Codex: passage only upon a sacred quest; then the page of the lowest quest ordained.</summary>
        void ReadCodex(Player me)
        {
            var data = me.m_customData;
            int quests = Get(data, ShrineRules.QuestsKey), done = Get(data, ShrineRules.DoneKey);
            if (!ShrineRules.MayPass(quests))
            {
                Say("\"" + D.Ovl(ShrineData.SNotOnQuest) + D.Ovl(ShrineData.SPassageDenied), ("Bye", () => { }));
                return;
            }
            int i = ShrineRules.ReadCodex(quests, ref done, out bool final);
            data[ShrineRules.DoneKey] = done.ToString();
            string page = D.Msg(ShrineData.SCodexLies) + "\n\n\"" + D.Ovl(ShrineData.SPassSeeker) + D.Msg(ShrineData.SBookOpen) + D.Msg(ShrineData.SPage)
                          + (i < 0 ? D.Msg(ShrineData.SHowDidYou) : "\"" + D.CodexWords(i) + "\"");
            Plugin.Log.LogInfo($"Shrines: {me.GetPlayerName()} reads the Codex on {(i < 0 ? "nothing" : D.Virtues[i])}{(final ? " (all eight)" : "")}");
            if (!final) { Say(page, ("Bye", () => { })); return; }
            Say(page, ("On", () =>
            {
                Quake();
                FinalPage(-1);
            }));
        }

        void FinalPage(int k)
        {
            string text = k < 0 ? D.Msg(ShrineData.SWind) + D.Ovl(ShrineData.SThouDostRead) : ShrineData.Runes(D.Msg(ShrineData.FinalPages[k]));
            if (k + 1 < ShrineData.FinalPages.Length) Say(text, ("On", () => FinalPage(k + 1)));
            else Say(text, ("Bye", () => { }));
        }

        void Say(string text, params (string, Action)[] answers)
            => TalkPanel.Ask(this, GetHoverName(), text.Replace("\r", "").Trim('\n'), answers);

        void Quake()
        {
            GameCamera.instance?.AddShake(transform.position, 50f, 2f, false);
        }

        static int Get(IDictionary<string, string> d, string key) => d.TryGetValue(key, out var v) && int.TryParse(v, out int n) ? n : 0;

        float checkAt;
        bool ruined;
        Fireplace altarFire;

        /// <summary>A ruined shrine's altar burns no more (the brazier's own fire keeps turning itself back on: hidden after it each frame).</summary>
        void LateUpdate()
        {
            if (IsCodex || Index < 0) return;
            if (Time.time >= checkAt)
            {
                checkAt = Time.time + 2f;
                ruined = Shrines.IsRuined(Index);
                if (altarFire == null)
                    altarFire = Physics.OverlapSphere(transform.position + Vector3.up, 3f).Select(c => c.GetComponentInParent<Fireplace>()).FirstOrDefault(f => f != null);
            }
            if (ruined && altarFire?.m_enabledObject != null && altarFire.m_enabledObject.activeSelf) altarFire.m_enabledObject.SetActive(false);
        }
    }
}
