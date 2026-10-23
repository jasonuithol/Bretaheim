using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Bretaheim.Data;
using Bretaheim.WorldGen.Core;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Bretaheim.WorldGen
{
    /// <summary>
    /// The introduction played in the world: "Return to the View" (Data/TheView, the title's
    /// animated demo) built in Valheim pieces and acted by Valheim figures, with the words of the
    /// "Ultima V Introduction" (Data/Story) under it at the moments they tell. Each of the View's
    /// four scenes (The Summoning, The Journey, The Arrival, The Welcoming) is its 19×4 strip of
    /// tiles planned as a settlement map (SettlementPlanner.PlanMap, TheView.Stage: forest round
    /// it, Iolo's hut walled in) and built as local objects on Valheim ground (the terrain's own
    /// material) high above where the player arrived, in a dark night of its own (the Arrival in a
    /// thunderstorm); the script moves its figures a tile at a time as the original does, slowed
    /// to a walk. The Avatar has the player's own face and hair. The player waits out of sight
    /// below the stage; Space goes on from a paragraph, Escape skips the rest. Nothing is
    /// networked and everything is cleaned up, also when it ends early.
    /// </summary>
    public static partial class IntroStage
    {
        public static bool Playing { get; private set; }
        /// <summary>How many times faster than life the next playing goes (u5 intro fast: for looking it over).</summary>
        public static float Speed = 1f;
        /// <summary>Above the player's spot, out of every other system's way (as the raising's stage).</summary>
        const float Height = 3000f, Below = 40f;
        /// <summary>Our seconds for one of the original's: its figures stride a 2 m tile in 7 ticks.</summary>
        const float Tempo = 2.2f;
        /// <summary>Words read a second; a page stays at least <see cref="MinRead"/> seconds.</summary>
        const float ReadRate = 4f, MinRead = 4f;
        /// <summary>The script's own pauses go quicker than its walking (<see cref="Tempo"/>): it waits on the words anyway.</summary>
        const float WaitTempo = 1.3f;
        /// <summary>Where in the View's script each paragraph of the Introduction begins to be told (offset → story screen).</summary>
        static readonly Dictionary<int, int> Cues = new Dictionary<int, int>
        {
            { 0x200, 0 }, { 0x20a, 1 }, { 0x213, 2 }, { 0x21b, 3 },
            { 0x2b2, 4 }, { 0x2e0, 5 }, { 0x2e2, 6 },
            { 0x31a, 7 }, { 0x329, 8 }, { 0x32a, 9 }, { 0x33e, 10 }, { 0x36d, 11 }, { 0x384, 12 }, { 0x39f, 13 },
            { 0x3de, 14 }, { 0x42f, 15 }, { 0x48c, 16 },
        };

        public static void Install(Harmony harmony)
        {
            // Escape skips the scene instead of opening the menu
            var update = AccessTools.Method(typeof(Menu), "Update");
            if (update != null) harmony.Patch(update, prefix: new HarmonyMethod(typeof(IntroStage), nameof(Menu_Prefix)));
        }

        static bool Menu_Prefix() => !Playing;

        static Director current;

        /// <summary>Hides (or shows again) the playing scene's objects whose name has this in it (for looking at the stage).</summary>
        public static string Toggle(string part)
        {
            var root = current?.Root;
            if (root == null) return "no scene playing";
            int n = 0;
            foreach (Transform t in root.transform)
                if (t.name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0) { t.gameObject.SetActive(!t.gameObject.activeSelf); n++; }
            return $"{n} toggled: " + string.Join(", ", root.transform.Cast<Transform>().Select(t => t.name).Distinct().Take(40));
        }

        /// <summary>Plays the introduction for the local player; false in <paramref name="done"/> when it could not be staged.</summary>
        public static IEnumerator Play(Player me, Action<bool> done, int fromScene = 0)
        {
            if (Playing || ResurrectionStage.Playing || me == null) { done?.Invoke(false); yield break; }
            // (back from a stage just played: wait until the player is home again)
            for (float t = 0f; t < 5f && (me.IsTeleporting() || me.transform.position.y > Height / 2); t += Time.deltaTime) yield return null;
            if (me.transform.position.y > Height / 2) { done?.Invoke(false); yield break; }
            Director d;
            try { d = new Director(me) { From = fromScene }; }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Intro stage: cannot stage the View: {e.Message}");
                done?.Invoke(false);
                yield break;
            }
            Playing = true;
            current = d;
            Plugin.Log.LogInfo($"Intro stage for {me.GetPlayerName()} at {d.Origin}");
            try { yield return d.Run(); }
            finally
            {
                d.Clean();
                Playing = false;
                Speed = 1f;
                Plugin.Log.LogInfo($"Intro stage over ({(d.Skipped ? "skipped" : "to the end")})");
            }
            done?.Invoke(true);
        }

        /// <summary>A figure of the scene (a monster-table slot of the original).</summary>
        class Actor
        {
            public GameObject Go;
            public Animator Anim;
            public string Who;
            public readonly Queue<Vector3> Path = new Queue<Vector3>();
            public float Speed = 2.4f;
            public bool Still;
            public Vector3 Pos => Go.transform.position;
        }

        /// <summary>The scene being played: its set, figures, words and camera.</summary>
        partial class Director
        {
            readonly Player me;
            readonly TheView view;
            readonly List<string> story;
            readonly List<StoryScreen> screens;
            readonly SettlementPlanner planner;
            public readonly Vector3 Origin;
            readonly Vector3 home;
            readonly Quaternion homeFacing;
            public bool Skipped;
            /// <summary>The scene to begin with (the ones before are passed over).</summary>
            public int From;
            bool hudWas;
            float started;

            GameObject root, slab;
            public GameObject Root => root;
            SettlementPlan plan;
            int scene = -1;
            readonly Actor[] slots = new Actor[TheView.Slots];
            readonly Dictionary<int, List<GameObject>> byCell = new Dictionary<int, List<GameObject>>();
            readonly Dictionary<int, (Vector3 at, Quaternion facing, bool bed)> seats = new Dictionary<int, (Vector3, Quaternion, bool)>();
            readonly List<GameObject> front = new List<GameObject>();
            readonly List<GameObject> gates = new List<GameObject>();
            Light moon, lamp;
            float nextFlash = float.MaxValue;
            bool tale, knocked;

            // the words
            GameObject ui;
            Image fader;
            Text title, words, hint;
            float titleUntil, shownAt, readFor, pageAt;
            int shown = -1, page;
            List<string> pages;
            bool next;

            Vector3 camAt, camLook;

            public Director(Player me)
            {
                this.me = me;
                var misc = ScenePlayer.Read("MISCMAPS.DAT") ?? throw new Exception("MISCMAPS.DAT not found");
                view = new TheView(misc, Plugin.U5.Ovl);
                screens = Story.Available(Plugin.U5.Directory) ? Story.Load(Plugin.U5.Directory, Plugin.U5.Ovl) : null;
                story = screens?.Select(s => s.Text).ToList();
                planner = Settlements.Planner();
                home = me.transform.position;
                homeFacing = me.transform.rotation;
                Origin = home + new Vector3((me.GetPlayerID() & 7) * 90f, Height, 0f);
            }

            public IEnumerator Run()
            {
                Stage.SafeSpot = home;
                started = Now;
                Stage.Darkness(true);
                BuildUi();
                SetFade(1f);
                // out of sight below the stage (a player just arrived may not teleport for 2 s)
                slab = Stage.Floor(Origin + Vector3.down * Below);
                for (float t = 0f; !me.TeleportTo(Origin + Vector3.down * (Below - 0.1f), Quaternion.identity, false) && t < 5f; t += Dt)
                {
                    yield return null;
                    if (!Here) yield break;
                }
                Stage.Held = true;

                if (Hud.instance != null) { hudWas = Hud.instance.m_userHidden; Hud.instance.m_userHidden = true; }
                bool passing = From > 0;
                // (begun later: the Avatar has the Ankh and the amulet already)
                ankhHeld = amuletHeld = From > 0;
                foreach (var ev in view.Run())
                {
                    if (!Here || Skipped) yield break;
                    if (ev.Kind == ViewEventKind.Scene) passing = ev.Scene < From;
                    if (passing) continue;
                    if (Cues.TryGetValue(ev.At, out int p) && ev.Kind != ViewEventKind.Scene) yield return Say(p);
                    Frame(ev);
                    // what the Introduction tells that the View does not show (Effects)
                    bool instead = false;
                    var before = Before(ev, () => instead = true);
                    if (before != null) yield return before;
                    if (instead) continue;
                    switch (ev.Kind)
                    {
                        case ViewEventKind.Scene:
                            if (scene >= 0) yield return Fade(1f, 1.2f);
                            Build(ev.Scene);
                            yield return Say(Cues.TryGetValue(ev.At, out int first) ? first : -1);
                            ShowTitle(view.Caption[ev.Scene]);
                            yield return Fade(0f, 1.5f);
                            break;
                        case ViewEventKind.Wait:
                            yield return Wait(ev.Frames * TheView.Tick * WaitTempo);
                            break;
                        case ViewEventKind.Place:
                            yield return Place(ev);
                            break;
                        case ViewEventKind.Remove:
                            Remove(ev.Slot);
                            break;
                        case ViewEventKind.Move:
                            Walk(ev.Slot, ev.X, ev.Y);
                            yield return Wait(Frames(ev.Frames));
                            break;
                        case ViewEventKind.Ground:
                            Ground(ev.X, ev.Y, ev.Tile);
                            break;
                        case ViewEventKind.GateOpens:
                            yield return Gate(ev.X, ev.Y, true, Frames(ev.Frames));
                            break;
                        case ViewEventKind.GateCloses:
                            yield return Gate(ev.X, ev.Y, false, Frames(ev.Frames));
                            break;
                        case ViewEventKind.DissolveIn:
                        case ViewEventKind.DissolveOut:
                            yield return Dissolve(ev.Slot, ev.Kind == ViewEventKind.DissolveIn, Frames(ev.Frames));
                            break;
                        case ViewEventKind.Bolt:
                            yield return Bolt(ev.Slot, Frames(ev.Frames));
                            break;
                    }
                    var after = After(ev);
                    if (after != null) yield return after;
                }
                // Iolo's tale, by the hearth
                tale = true;
                if (story != null)
                    for (int p = 17; p < story.Count && Here && !Skipped; p++) yield return Say(p);
                yield return Say(-1);
                if (Here && !Skipped) yield return Fade(1f, 2f);
            }

            static float Now => Time.time * Speed;
            static float Dt => Time.deltaTime * Speed;

            bool Here => me != null && me == Player.m_localPlayer && !me.IsDead();

            static float Frames(int n) => n * TheView.Tick * Tempo;

            // ── time ──────────────────────────────────────────────────────────────────────────

            /// <summary>One frame of the scene: the figures walk, the camera follows, the keys are read.</summary>
            void Tick()
            {
                float dt = Dt;
                foreach (var a in slots)
                {
                    if (a?.Go == null || a.Still) continue;
                    float speed = 0f;
                    if (a.Path.Count > 0)
                    {
                        var to = a.Path.Peek();
                        var d = to - a.Pos;
                        d.y = 0;
                        float step = a.Speed * dt;
                        if (d.magnitude <= step) { a.Go.transform.position = to; a.Path.Dequeue(); }
                        else a.Go.transform.position += d.normalized * step;
                        if (d.sqrMagnitude > 0.0001f) a.Go.transform.rotation = Quaternion.Slerp(a.Go.transform.rotation, Quaternion.LookRotation(d), Mathf.Min(1f, dt * 10f));
                        speed = a.Speed;
                    }
                    if (a.Anim != null)
                    {
                        a.Anim.SetFloat("forward_speed", speed);
                        a.Anim.SetBool("onGround", true);
                    }
                }
                // the storm
                if (Now > nextFlash)
                {
                    nextFlash = Now + UnityEngine.Random.Range(4f, 10f);
                    Plugin.Instance.StartCoroutine(Flash(Origin + new Vector3(UnityEngine.Random.Range(-30f, 30f), 0f, UnityEngine.Random.Range(5f, 30f)), UnityEngine.Random.Range(0.4f, 0.8f)));
                }
                // the camera (Camera: the shot of the moment, else above and south of the figures)
                MoveCamera(dt);
                TickEffects();
                // the words
                if (title != null && titleUntil > 0)
                {
                    float left = titleUntil - Now;
                    var c = title.color;
                    c.a = Mathf.Clamp01(left / 1.5f);
                    title.color = c;
                    if (left <= 0) titleUntil = 0;
                }
                if (ZInput.GetKeyDown(KeyCode.Escape)) Skipped = true;
                if (ZInput.GetKeyDown(KeyCode.Space) || ZInput.GetKeyDown(KeyCode.E) || ZInput.GetButtonDown("JoyUse")) next = true;
                TurnPages();
            }

            /// <summary>What the camera looks at: the Avatar, else everyone on the stage, else the strip's middle; Iolo's table for his tale.</summary>
            Vector3 Focus()
            {
                if (tale) return Cell(10, 2) + Vector3.up * 0.8f;
                var avatar = slots.FirstOrDefault(a => a?.Go != null && a.Who.StartsWith("avatar"));
                Vector3 at;
                if (avatar != null) at = avatar.Pos;
                else
                {
                    var all = slots.Where(a => a?.Go != null && a.Who != "ankh").ToList();
                    at = all.Count > 0 ? all.Aggregate(Vector3.zero, (s, a) => s + a.Pos) / all.Count : Cell(9, 2);
                }
                // keep the strip in sight
                var left = Cell(4, 2); var right = Cell(14, 2);
                at.x = Mathf.Clamp(at.x, left.x, right.x);
                at.z = Cell(0, 2).z;
                return at + Vector3.up * 0.8f;
            }

            IEnumerator Wait(float seconds)
            {
                float until = Now + seconds;
                do
                {
                    Tick();
                    yield return null;
                } while (Now < until && Here && !Skipped);
            }

            /// <summary>
            /// The next paragraph of the Introduction (−1: none), once the last has been read: its
            /// pages turn by themselves as long as each takes to read (Space turns one sooner), the
            /// figures moving meanwhile; a scene waits only for its paragraph's last page.
            /// </summary>
            IEnumerator Say(int p)
            {
                float held = Now;
                while (shown >= 0 && !Read() && Here && !Skipped)
                {
                    Tick();
                    yield return null;
                }
                if (Speed != 1f && shown >= 0) Plugin.Log.LogInfo($"Intro timing: paragraph {shown} was up {Now - shownAt:0.0} s ({pages?.Count ?? 0} pages), the scene held {Now - held:0.0} s for it; at {Now - started:0.0} s");
                next = false;
                if (p < 0 || story == null || p >= story.Count)
                {
                    if (words != null) words.transform.parent.gameObject.SetActive(false);
                    shown = -1;
                    pages = null;
                    yield break;
                }
                shown = p;
                shownAt = Now;
                Illustrate(p);
                pages = Story.Pages(story[p]);
                ShowPage(0);
            }

            /// <summary>The paragraph's last page has been read (or Space pressed on it).</summary>
            bool Read() => pages == null || (page >= pages.Count - 1 && (next || Now >= pageAt + readFor));

            /// <summary>Every frame: the page turned once read.</summary>
            void TurnPages()
            {
                if (pages == null || page >= pages.Count - 1) return;
                if (next || Now >= pageAt + readFor)
                {
                    next = false;
                    ShowPage(page + 1);
                }
            }

            void ShowPage(int i)
            {
                page = i;
                pageAt = Now;
                string text = pages[i];
                readFor = Mathf.Max(MinRead, Story.Words(text) / ReadRate);
                if (tale) TaleShot(shown * 3 + i);
                if (words == null) return;
                words.transform.parent.gameObject.SetActive(true);
                words.text = text;
                float h = LayoutUtility.GetPreferredHeight(words.rectTransform);
                words.rectTransform.sizeDelta = new Vector2(words.rectTransform.sizeDelta.x, h);
                ((RectTransform)words.transform.parent).sizeDelta = new Vector2(((RectTransform)words.transform.parent).sizeDelta.x, h + 22);
            }

            // ── the set ───────────────────────────────────────────────────────────────────────

            Vector3 Cell(int c, int r) => Origin + new Vector3(plan.LocalX(TheView.StageX + c), 0f, plan.LocalZ(TheView.StageY + r));

            int MapCell(int c, int r) => (TheView.StageY + r) * TheView.StageSize + TheView.StageX + c;

            void Build(int n)
            {
                ClearSet();
                scene = n;
                var raw = view.Stage(n);
                plan = planner.PlanMap(raw, "dwelling", VBiome.Meadows, false);
                root = new GameObject($"u5_intro_scene{n}");
                root.transform.position = Origin;
                var ground = Ground(raw);
                if (ground != null) ground.transform.SetParent(root.transform, true);
                int built = 0;
                foreach (var p in plan.Pieces)
                {
                    var go = Piece(p);
                    if (go == null) continue;
                    built++;
                    int cell = plan.CellAt(p.X, p.Z);
                    if (!byCell.TryGetValue(cell, out var list)) byCell[cell] = list = new List<GameObject>();
                    list.Add(go);
                    if (n == 3 && cell / TheView.StageSize == TheView.StageY + TheView.Rows) front.Add(go);
                }
                // the hut is seen into, but for the Avatar's knock at its door
                foreach (var go in front) go.SetActive(false);
                Light(n);
                SetDressing(n);
                Plugin.Log.LogInfo($"Intro stage: \"{view.Caption[n]}\" built of {built} of {plan.Pieces.Count} pieces");
                // the scene's first shot, the camera easing in to it from a little further off
                fill = null;
                Opening(n);
                cutting = true;
                MoveCamera(0f);
                camAt += (camAt - camLook).normalized * 2.5f + Vector3.up * 0.5f;
            }

            /// <summary>One piece of the plan, its look only; a bed's or a chair's place to lie or sit kept.</summary>
            GameObject Piece(Placement p)
            {
                string name = p.Prefab;
                float fit = 0f;
                // the circle of stones: standing stones, not heaps
                if (name == "stone_pile" && scene == 1) { name = "RockFinger"; fit = 2.2f; }
                // near the strip, trees the onlooker sees over (a beech's crown would fill the view)
                int row = plan.CellAt(p.X, p.Z) / TheView.StageSize;
                if ((name == "Beech1" || name == "Oak1") && row >= TheView.StageY - 2)
                    name = (p.X + p.Z) % 3 < 1 ? "Bush01" : (p.X + p.Z) % 3 < 2 ? "Beech_small1" : "Beech_small2";
                var prefab = ZNetScene.instance.GetPrefab(name);
                if (prefab == null) return null;
                var rot = Quaternion.Euler(0, p.Yaw, 0);
                if (p.Turned) rot *= Quaternion.Euler(p.Tilt, 0, 0) * Quaternion.Euler(0, p.Spin, 0) * Quaternion.Euler(0, 0, p.Roll);
                var pos = Origin + new Vector3(p.X, p.Y, p.Z);
                var scale = p.Scaled ? new Vector3(p.SX, p.SY, p.SZ) : Vector3.one;
                Seat(prefab, pos, rot, scale, p);
                var go = Stage.Still(prefab, pos, rot, 1f, 1f, root.transform);
                go.transform.localScale = Vector3.Scale(prefab.transform.localScale, scale);
                if (fit > 0) Fit(go, fit);
                if (p.Look != null)
                {
                    try { Looks.Apply(go, p.Look); } catch (Exception) { }
                    foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) if (!(mb is LODGroup) && !(mb is U5Look)) Object.Destroy(mb);
                }
                // fires burn (but the Summoning's hearth: a cold night, all asleep)
                if (scene != 0 && (name == "fire_pit" || name == "hearth" || name == "piece_walltorch")) DungeonLooks.Burn(go);
                Remember(name, go);
                return go;
            }

            /// <summary>A piece scaled to stand <paramref name="height"/> m tall, its foot on the ground.</summary>
            void Fit(GameObject go, float height)
            {
                var rs = go.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) return;
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                if (b.size.y < 0.01f) return;
                go.transform.localScale *= height / b.size.y;
                go.transform.position = new Vector3(go.transform.position.x, Origin.y - 0.3f + (go.transform.position.y - b.min.y) * height / b.size.y, go.transform.position.z);
            }

            void Seat(GameObject prefab, Vector3 pos, Quaternion rot, Vector3 scale, Placement p)
            {
                Transform point = null;
                bool bed = false;
                var b = prefab.GetComponentInChildren<Bed>(true);
                if (b != null && b.m_spawnPoint != null) { point = b.m_spawnPoint; bed = true; }
                var ch = prefab.GetComponentInChildren<Chair>(true);
                if (point == null && ch != null && ch.m_attachPoint != null) point = ch.m_attachPoint;
                if (point == null) return;
                var t = prefab.transform;
                var local = Quaternion.Inverse(t.rotation) * (point.position - t.position);
                var facing = rot * (Quaternion.Inverse(t.rotation) * point.rotation);
                var at = pos + rot * Vector3.Scale(local, scale) + (bed ? Vector3.up * 0.5f : Vector3.zero);
                seats[plan.CellAt(p.X, p.Z)] = (at, facing, bed);
            }

            /// <summary>
            /// The ground: a quad a tile (but where the map has nothing), in four lit materials as
            /// the plan paints it (grass and earth of their own noisy colours on a stone floor's
            /// shader, paving as the stone floor, planks as the wood floor); water sunk, with a dark
            /// surface over it.
            /// </summary>
            GameObject Ground(byte[] raw)
            {
                int n = TheView.StageSize;
                var v = new List<Vector3>(); var uv = new List<Vector2>(); var wet = new List<int>();
                var subs = new[] { new List<int>(), new List<int>(), new List<int>(), new List<int>() };
                float half = plan.Tile / 2;
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int c = y * n + x;
                    if (raw[c] == TheView.Nothing) continue;
                    var paint = plan.GroundPaint[c];
                    bool water = plan.Water[c];
                    int sub = water || paint == Paint.Dirt || paint == Paint.Cultivated ? 1 : paint == Paint.Paved ? 2 : paint == Paint.Planks ? 3 : 0;
                    float cx = plan.LocalX(x), cz = plan.LocalZ(y), h = water ? -0.8f : 0f;
                    int i = v.Count;
                    foreach (var (dx, dz) in new[] { (-1, -1), (-1, 1), (1, 1), (1, -1) })
                    {
                        v.Add(new Vector3(cx + dx * half, h, cz + dz * half));
                        // (a texture's worth every two tiles)
                        uv.Add(new Vector2((x + (dx + 1) / 2f) / 2f, (n - y + (dz - 1) / 2f) / 2f));
                    }
                    foreach (int k in new[] { i, i + 1, i + 2, i, i + 2, i + 3 }) subs[sub].Add(k);
                    if (water) foreach (int k in new[] { i, i + 1, i + 2, i, i + 2, i + 3 }) wet.Add(k);
                }
                if (v.Count == 0) return null;
                var go = new GameObject("u5_intro_ground");
                go.transform.position = Origin;
                var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.SetVertices(v); mesh.SetUVs(0, uv);
                mesh.subMeshCount = subs.Length;
                for (int k = 0; k < subs.Length; k++) mesh.SetTriangles(subs[k], k);
                mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = GroundLooks();
                if (wet.Count > 0)
                {
                    // the water's surface just under the banks
                    var w = new GameObject("u5_intro_water");
                    w.transform.SetParent(go.transform, false);
                    w.transform.localPosition = Vector3.up * 0.55f;
                    var wm = new Mesh();
                    wm.SetVertices(v); wm.SetTriangles(wet, 0); wm.RecalculateNormals(); wm.RecalculateBounds();
                    w.AddComponent<MeshFilter>().sharedMesh = wm;
                    var shader = Shader.Find("Sprites/Default");
                    w.AddComponent<MeshRenderer>().sharedMaterial = shader != null ? new Material(shader) { color = new Color(0.03f, 0.07f, 0.13f, 0.9f) } : DungeonLooks.Materials(DungeonStyle.Cave)[3];
                }
                return go;
            }

            static Material[] groundLooks;

            /// <summary>Grass, earth, paving and planks for the ground (made once).</summary>
            static Material[] GroundLooks()
            {
                if (groundLooks != null) return groundLooks;
                Material From(string prefab) => Structures.Template(prefab)?.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(r => r.sharedMaterial != null)?.sharedMaterial;
                var stone = From("stone_floor_2x2");
                var wood = From("wood_floor") ?? stone;
                // (a piece's shader draws a flat mesh over everything: Unity's own lit shader, if the game has it)
                var lit = Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse") ?? Shader.Find("Diffuse");
                Plugin.Log.LogInfo($"Intro stage: ground shader {(lit != null ? lit.name : "none")}");
                Material Noisy(Color a, Color b, int seed)
                {
                    if (lit == null && stone == null) return null;
                    var m = lit != null ? new Material(lit) { name = "u5_intro_ground" } : new Material(stone) { name = "u5_intro_ground" };
                    if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.05f);
                    var tex = new Texture2D(128, 128, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
                    var rng = new System.Random(seed);
                    for (int y = 0; y < 128; y++)
                    for (int x = 0; x < 128; x++)
                    {
                        float f = Mathf.PerlinNoise(x / 9f + seed, y / 9f) * 0.6f + (float)rng.NextDouble() * 0.4f;
                        tex.SetPixel(x, y, Color.Lerp(a, b, f));
                    }
                    tex.Apply();
                    m.mainTexture = tex;
                    if (m.HasProperty("_Color")) m.color = Color.white;
                    foreach (var bump in new[] { "_BumpMap", "_MetallicGlossMap", "_EmissionMap" }) if (m.HasProperty(bump)) m.SetTexture(bump, null);
                    return m;
                }
                groundLooks = new[]
                {
                    Noisy(new Color(0.08f, 0.12f, 0.05f), new Color(0.19f, 0.25f, 0.11f), 3),
                    Noisy(new Color(0.16f, 0.11f, 0.06f), new Color(0.3f, 0.22f, 0.13f), 7),
                    Noisy(new Color(0.25f, 0.24f, 0.23f), new Color(0.42f, 0.4f, 0.37f), 11),
                    Noisy(new Color(0.3f, 0.2f, 0.11f), new Color(0.42f, 0.3f, 0.17f), 13),
                };
                return groundLooks;
            }

            /// <summary>Night for the bedroom and the hut, moonlight in the woods, the storm at the arrival.</summary>
            void Light(int n)
            {
                var env = EnvMan.instance;
                Color sky = n == 2 ? new Color(0.02f, 0.02f, 0.03f) : new Color(0.01f, 0.015f, 0.035f);
                string name = n == 2 ? "U5_Storm" : n == 0 ? "U5_Bedroom" : "U5_Night";
                if (env != null)
                {
                    if (n == 2) Stage.Environment(env, name, new Color(0.2f, 0.21f, 0.25f), sky, 0.03f, "ThunderStorm");
                    else if (n == 0) Stage.Environment(env, name, new Color(0.2f, 0.21f, 0.3f), Color.black, 0.02f);
                    else Stage.Environment(env, name, new Color(0.22f, 0.25f, 0.36f), sky, 0.02f, "Clear");
                }
                Stage.Darkness(true, sky, 70f, name);
                var go = new GameObject("u5_intro_moon");
                go.transform.SetParent(root.transform, false);
                go.transform.rotation = Quaternion.Euler(55f, -25f, 0f);
                moon = go.AddComponent<Light>();
                moon.type = LightType.Directional;
                moon.color = new Color(0.55f, 0.65f, 1f);
                moon.intensity = n == 0 ? 0.15f : n == 2 ? 0.3f : n == 3 ? 0.45f : 0.75f;
                moon.shadows = n == 1 ? LightShadows.Soft : LightShadows.None;
                // (a point light, not a spot: no edge to its pool)
                lamp = Stage.Glow(Origin + Vector3.up * 7f, n == 3 ? new Color(1f, 0.82f, 0.62f) : new Color(0.72f, 0.78f, 1f), n == 0 ? 0.7f : n == 3 ? 0.6f : 1.1f, 24f, root.transform).GetComponent<Light>();
                if (n == 0)
                {
                    // moonlight through the west window onto the bed
                    var window = Cell(2, 2);
                    windowLight = Stage.Spot(window + new Vector3(-3f, 4f, 0f), Cell(3, 3), new Color(0.6f, 0.7f, 1f), 3.5f, 16f, 70f, root.transform).GetComponent<Light>();
                }
                nextFlash = n == 2 ? Now + 2f : float.MaxValue;
            }

            IEnumerator Flash(Vector3 at, float strength)
            {
                if (moon == null) yield break;
                var flash = Stage.Glow(at + Vector3.up * 25f, new Color(0.85f, 0.9f, 1f), 6f * strength, 80f, root.transform);
                float was = moon.intensity;
                moon.intensity = 1.6f * strength;
                yield return new WaitForSeconds(0.08f);
                if (moon != null) moon.intensity = was * 2f;
                yield return new WaitForSeconds(0.06f);
                if (moon != null) moon.intensity = 1.2f * strength;
                yield return new WaitForSeconds(0.1f);
                if (moon != null) moon.intensity = was;
                if (flash != null) Object.Destroy(flash);
                yield return new WaitForSeconds(UnityEngine.Random.Range(0.3f, 1.2f));
                if (Playing) Stage.Thunder(camAt, strength);
            }

            void ClearSet()
            {
                foreach (var a in slots) if (a?.Go != null) Object.Destroy(a.Go);
                Array.Clear(slots, 0, slots.Length);
                foreach (var g in gates) if (g != null) Object.Destroy(g);
                gates.Clear();
                if (root != null) Object.Destroy(root);
                root = null;
                byCell.Clear(); seats.Clear(); front.Clear();
                ClearEffects();
                nextFlash = float.MaxValue;
            }

            /// <summary>A door opens (the floor tile in its place) or shuts (the door tile back); a mirror's flash is not played.</summary>
            void Ground(int x, int y, int tile)
            {
                if (!byCell.TryGetValue(MapCell(x, y), out var list)) return;
                foreach (var go in list)
                    if (go != null && go.name.IndexOf("door", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (go.activeSelf != (tile == TheView.Door)) DoorSound(go.transform.position, tile != TheView.Door);
                        go.SetActive(tile == TheView.Door);
                    }
            }

            // ── the figures ───────────────────────────────────────────────────────────────────

            /// <summary>Who a slot's tile is in this scene.</summary>
            string Who(int tile)
            {
                switch (tile + 0x100)
                {
                    case 0x11a: return scene == 0 ? "avatar_asleep" : "shamino_abed";
                    case 0x116: return "glow";
                    case 0x150: return scene == 0 ? "avatar_night" : "iolo";
                    case 0x14c: return "avatar";
                    case 0x131: return "avatar_seated";
                    case 0x144: return "shamino";
                    case 0x11e: return "shamino_down";
                    case 0x132: return "iolo_lute";
                    case 0x1fc: return "shadowlord";
                    case 0x105:
                    case 0x106: return "ankh";
                    default: return "someone";
                }
            }

            IEnumerator Place(ViewEvent ev)
            {
                string who = Who(ev.Tile);
                var at = Cell(ev.X, ev.Y);
                var a = slots[ev.Slot];
                // the same one walks on to where the script puts them
                if (a?.Go != null && a.Who == who)
                {
                    Walk(ev.Slot, ev.X, ev.Y);
                    yield break;
                }
                Remove(ev.Slot);
                // the Avatar and Shamino come to Iolo's door from the forest, and knock
                if (scene == 3 && who == "avatar" && !knocked)
                {
                    knocked = true;
                    foreach (var go in front) if (go != null) go.SetActive(true);
                    var avatar = slots[ev.Slot] = Make(who, Cell(8, 8));
                    var shamino = slots[1] = Make("shamino", Cell(9, 9));
                    if (shamino != null) shamino.Speed = avatar.Speed = 1.6f;
                    foreach (int r in new[] { 7, 6, 5 }) avatar.Path.Enqueue(Cell(8, r));
                    foreach (int r in new[] { 8, 7, 6 }) shamino?.Path.Enqueue(Cell(8, r) + Vector3.right * 0.9f);
                    while (avatar.Path.Count > 0 && Here && !Skipped) { Tick(); yield return null; }
                    avatar.Go.transform.rotation = Quaternion.LookRotation(Vector3.forward);
                    Stage.Pose(avatar.Go, "challenge");
                    Knock(Cell(8, 4));
                    yield return Wait(2.5f);
                    // the door jerks open: the front of the hut is taken away to see in
                    Ground(8, 4, 0x44);
                    foreach (var go in front) if (go != null) go.SetActive(false);
                    avatar.Speed = shamino != null ? shamino.Speed = 2.4f : 2.4f;
                    foreach (int r in new[] { 4, 3 }) avatar.Path.Enqueue(Cell(8, r));
                    yield break;
                }
                slots[ev.Slot] = Make(who, at, ev.X, ev.Y);
            }

            Actor Make(string who, Vector3 at, int x = -1, int y = -1)
            {
                var a = new Actor { Who = who };
                var facing = Quaternion.LookRotation(Vector3.back);
                string[] avatarDress = { "ArmorLeatherChest", "ArmorLeatherLegs", "CapeDeerHide" };
                switch (who)
                {
                    case "avatar_night":
                        a.Go = Stage.Figure(at, facing, new[] { "ArmorRagsChest", "ArmorRagsLegs" }, root.transform);
                        break;
                    case "avatar_asleep":
                        a.Go = Stage.Figure(at, facing, new[] { "ArmorRagsChest", "ArmorRagsLegs" }, root.transform);
                        Sit(a, x, y, "attach_bed");
                        break;
                    case "avatar":
                        a.Go = Stage.Figure(at, facing, avatarDress, root.transform);
                        break;
                    case "avatar_seated":
                        a.Go = Stage.Figure(at, facing, avatarDress, root.transform);
                        Sit(a, x, y, "attach_chair");
                        break;
                    case "shamino":
                        a.Go = Stage.Figure(at, facing, new[] { "HelmetTrollLeather", "ArmorTrollLeatherChest", "ArmorTrollLeatherLegs", "CapeTrollHide" }, root.transform);
                        break;
                    case "shamino_abed":
                        a.Go = Stage.Figure(at, facing, new[] { "ArmorTrollLeatherChest", "ArmorTrollLeatherLegs" }, root.transform);
                        Sit(a, x, y, "attach_bed");
                        break;
                    case "shamino_down":
                        a.Go = Stage.Figure(at + Vector3.up * 0.15f, Quaternion.LookRotation(Vector3.left), new[] { "HelmetTrollLeather", "ArmorTrollLeatherChest", "ArmorTrollLeatherLegs", "CapeTrollHide" }, root.transform);
                        a.Still = true;
                        a.Anim = a.Go?.GetComponentInChildren<Animator>();
                        a.Anim?.SetBool("attach_bed", true);
                        break;
                    case "iolo":
                        a.Go = Stage.Figure(at, facing, new[] { "HelmetMidsummerCrown", "ArmorLeatherChest", "ArmorRagsLegs", "CapeLinen" }, root.transform);
                        break;
                    case "iolo_lute":
                        a.Go = Stage.Figure(at, facing, new[] { "HelmetMidsummerCrown", "ArmorLeatherChest", "ArmorRagsLegs", "CapeLinen" }, root.transform);
                        Sit(a, x, y, "attach_chair");
                        break;
                    case "shadowlord":
                        a.Go = Shadowlord(at);
                        a.Speed = 1.4f;
                        break;
                    case "glow":
                        a.Go = Circle(at + Vector3.up * 2.4f);
                        a.Still = true;
                        break;
                    case "ankh" when caseAnkh != null:
                        a.Go = new GameObject("u5_intro_nobody");
                        a.Go.transform.SetParent(root.transform, false);
                        a.Still = true;
                        Plugin.Instance.StartCoroutine(OpenCase());
                        break;
                    case "ankh":
                        // the Ankh lifted from its case: a sparkle and its gleam
                        Stage.Effect("vfx_odin_despawn", at + Vector3.up * 0.8f, root.transform);
                        a.Go = Stage.Glow(at + Vector3.up * 1.2f, new Color(1f, 0.85f, 0.4f), 1.5f, 4f, root.transform);
                        a.Still = true;
                        break;
                    default:
                        a.Go = Stage.Figure(at, facing, new string[0], root.transform);
                        break;
                }
                if (a.Go == null) a.Go = new GameObject("u5_intro_nobody");
                if (a.Anim == null) a.Anim = a.Go.GetComponentInChildren<Animator>();
                if (who.StartsWith("avatar")) { Stage.Likeness(a.Go, me); Carry(a); }
                if (who == "shadowlord") Smoke(at, 2.5f);
                return a;
            }

            /// <summary>A figure lying in the bed or sitting on the chair of its cell (or the nearest one).</summary>
            void Sit(Actor a, int x, int y, string pose)
            {
                if (a.Go == null) return;
                a.Still = true;
                int cell = MapCell(x, y);
                bool bed = pose == "attach_bed";
                var near = seats.Where(s => s.Value.bed == bed)
                    .OrderBy(s => Mathf.Abs(s.Key % TheView.StageSize - cell % TheView.StageSize) + Mathf.Abs(s.Key / TheView.StageSize - cell / TheView.StageSize))
                    .Select(s => (KeyValuePair<int, (Vector3 at, Quaternion facing, bool bed)>?)s).FirstOrDefault();
                if (near != null)
                {
                    a.Go.transform.position = near.Value.Value.at;
                    a.Go.transform.rotation = near.Value.Value.facing;
                }
                a.Anim = a.Go.GetComponentInChildren<Animator>();
                if (a.Anim != null)
                {
                    a.Anim.SetBool(pose, true);
                    a.Anim.SetBool("onGround", true);
                }
            }

            void Walk(int slot, int x, int y)
            {
                var a = slots[slot];
                if (a?.Go == null) return;
                if (a.Path.Count == 0) a.Anim?.SetTrigger("emote_stop");
                a.Path.Enqueue(Cell(x, y));
            }

            void Remove(int slot)
            {
                var a = slots[slot];
                if (a?.Go != null) Object.Destroy(a.Go);
                slots[slot] = null;
            }

            /// <summary>A Shadowlord: a wraith, black as pitch, with a cold violet glimmer.</summary>
            GameObject Shadowlord(Vector3 at)
            {
                var prefab = ZNetScene.instance.GetPrefab("Wraith") ?? ZNetScene.instance.GetPrefab("Ghost");
                if (prefab == null) return null;
                var go = Stage.Still(prefab, at, Quaternion.LookRotation(Vector3.right), 1f, 0.6f, root.transform);
                if (Recolour.Draws) Recolour.Model(go, new Color(0.12f, 0.08f, 0.16f));
                Stage.Glow(at + Vector3.up * 1.6f, new Color(0.5f, 0.25f, 0.9f), 1.2f, 4f, go.transform);
                return go;
            }

            /// <summary>The shimmering circle over the bed: a moongate's swirl, small, hovering.</summary>
            GameObject Circle(Vector3 at)
            {
                var gate = MoongateKeeper.Build(at);
                if (gate == null) return Stage.Glow(at, new Color(0.45f, 0.6f, 1f), 2f, 6f, root.transform);
                gate.transform.SetParent(root.transform, true);
                gate.transform.position = at + Vector3.down * 0.7f;
                gate.transform.rotation = Quaternion.LookRotation(Vector3.forward);
                gate.transform.localScale = Vector3.one * 0.22f;
                Blue(gate);
                // only the shimmering circle, not the portal's stones
                foreach (var r in gate.GetComponentsInChildren<MeshRenderer>(true)) r.enabled = false;
                return gate;
            }

            /// <summary>The shimmering blue door: the portal's fire turned blue.</summary>
            static void Blue(GameObject gate)
            {
                var blue = new Color(0.35f, 0.55f, 1f);
                foreach (var ps in gate.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    float a = main.startColor.color.a;
                    main.startColor = new Color(blue.r, blue.g, blue.b, a);
                    var col = ps.colorOverLifetime;
                    if (col.enabled) col.enabled = false;
                    // (the gradient-mapped flames and runes take their colours from custom data)
                    var custom = ps.customData;
                    if (custom.enabled)
                        foreach (var stream in new[] { ParticleSystemCustomData.Custom1, ParticleSystemCustomData.Custom2 })
                            if (custom.GetMode(stream) == ParticleSystemCustomDataMode.Color) custom.SetColor(stream, Rehue(custom.GetColor(stream)));
                    if (ps.trails.enabled)
                    {
                        var trails = ps.trails;
                        trails.colorOverLifetime = Rehue(trails.colorOverLifetime);
                        trails.colorOverTrail = Rehue(trails.colorOverTrail);
                    }
                }
                foreach (var l in gate.GetComponentsInChildren<Light>(true)) l.color = blue;
                // (the runes keep a red of their own: not shown)
                foreach (var ps in gate.GetComponentsInChildren<ParticleSystemRenderer>(true))
                    if (ps.name.IndexOf("Runes", StringComparison.OrdinalIgnoreCase) >= 0) ps.enabled = false;
                foreach (var r in gate.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.materials)
                    {
                        if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", blue * 2f);
                        // (the swirl's colours come from a gradient texture)
                        for (int k = 0; k < m.shader.GetPropertyCount(); k++)
                        {
                            string prop = m.shader.GetPropertyName(k);
                            if (m.shader.GetPropertyType(k) == UnityEngine.Rendering.ShaderPropertyType.Texture
                                && (prop.IndexOf("grad", StringComparison.OrdinalIgnoreCase) >= 0 || prop.IndexOf("ramp", StringComparison.OrdinalIgnoreCase) >= 0))
                                m.SetTexture(prop, BlueRamp());
                        }
                        if (r is ParticleSystemRenderer || m.shader.name.IndexOf("Particle", StringComparison.OrdinalIgnoreCase) >= 0 || m.shader.name.IndexOf("Additive", StringComparison.OrdinalIgnoreCase) >= 0 || m.shader.name.IndexOf("Unlit", StringComparison.OrdinalIgnoreCase) >= 0)
                            foreach (var prop in new[] { "_Color", "_TintColor" })
                                if (m.HasProperty(prop)) { var c = m.GetColor(prop); m.SetColor(prop, new Color(blue.r, blue.g, blue.b, c.a)); }
                    }
            }

            /// <summary>Colours turned blue, each as bright as it was.</summary>
            static ParticleSystem.MinMaxGradient Rehue(ParticleSystem.MinMaxGradient g)
            {
                Color C(Color c) { float v = Mathf.Max(c.r, Mathf.Max(c.g, c.b)); return new Color(v * 0.32f, v * 0.58f, v, c.a); }
                Gradient G(Gradient x)
                {
                    if (x == null) return null;
                    var y = new Gradient { mode = x.mode };
                    y.SetKeys(x.colorKeys.Select(k => new GradientColorKey(C(k.color), k.time)).ToArray(), x.alphaKeys);
                    return y;
                }
                switch (g.mode)
                {
                    case ParticleSystemGradientMode.Color: return new ParticleSystem.MinMaxGradient(C(g.color));
                    case ParticleSystemGradientMode.TwoColors: return new ParticleSystem.MinMaxGradient(C(g.colorMin), C(g.colorMax));
                    case ParticleSystemGradientMode.TwoGradients: return new ParticleSystem.MinMaxGradient(G(g.gradientMin), G(g.gradientMax));
                    default:
                        var r = new ParticleSystem.MinMaxGradient(G(g.gradient));
                        r.mode = g.mode;
                        return r;
                }
            }

            static Texture2D blueRamp;

            /// <summary>Dark to blue to white, for the gate's gradient-mapped particles.</summary>
            static Texture2D BlueRamp()
            {
                if (blueRamp != null) return blueRamp;
                blueRamp = new Texture2D(256, 4, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int x = 0; x < 256; x++)
                {
                    float f = x / 255f;
                    var c = f < 0.6f ? Color.Lerp(new Color(0f, 0.02f, 0.1f, 0f), new Color(0.2f, 0.4f, 1f, 1f), f / 0.6f) : Color.Lerp(new Color(0.2f, 0.4f, 1f, 1f), new Color(0.85f, 0.95f, 1f, 1f), (f - 0.6f) / 0.4f);
                    for (int y = 0; y < 4; y++) blueRamp.SetPixel(x, y, c);
                }
                blueRamp.Apply();
                return blueRamp;
            }

            IEnumerator Dissolve(int slot, bool appearing, float seconds)
            {
                var a = slots[slot];
                if (a?.Go == null) { yield return Wait(seconds); yield break; }
                if (a.Who == "shadowlord") Smoke(a.Pos, seconds + 1f);
                else Stage.Effect("vfx_odin_despawn", a.Pos + Vector3.up, root.transform);
                var full = a.Go.transform.localScale;
                if (appearing) a.Go.transform.localScale = full * 0.01f;
                float start = Now;
                while (Now < start + seconds && Here && !Skipped)
                {
                    float f = Mathf.Clamp01((Now - start) / seconds);
                    if (a.Go != null) a.Go.transform.localScale = full * Mathf.Max(0.01f, appearing ? f : 1f - f);
                    Tick();
                    yield return null;
                }
                if (a.Go != null) a.Go.transform.localScale = appearing ? full : full * 0.01f;
            }

            /// <summary>The moongate rising out of the ground (or sinking back), its light and hum.</summary>
            IEnumerator Gate(int x, int y, bool opens, float seconds)
            {
                var at = Cell(x, y);
                GameObject gate;
                if (opens)
                {
                    gate = MoongateKeeper.Build(at);
                    if (gate == null) { yield return Wait(seconds); yield break; }
                    gate.transform.SetParent(root.transform, true);
                    gate.transform.rotation = Quaternion.LookRotation(Vector3.back);
                    Blue(gate);
                    // the shimmering blue door alone: the portal's stones (and their red runes) not shown
                    foreach (var r in gate.GetComponentsInChildren<MeshRenderer>(true))
                        if (r.sharedMaterial != null && r.sharedMaterial.name.StartsWith("Portal_Metal")) r.enabled = false;
                    gates.Add(gate);
                    Stage.Effect("vfx_odin_despawn", at + Vector3.up, root.transform);
                }
                else
                {
                    gate = gates.OrderBy(g => g == null ? float.MaxValue : (g.transform.position - at).sqrMagnitude).FirstOrDefault();
                    if (gate == null) { yield return Wait(seconds); yield break; }
                }
                float start = Now;
                seconds = Mathf.Max(seconds, 2.5f);
                while (Now < start + seconds && Here && !Skipped)
                {
                    float f = Mathf.Clamp01((Now - start) / seconds);
                    float h = opens ? f : 1f - f;
                    if (gate != null) gate.transform.position = at + Vector3.down * 6f * (1f - h);
                    Tick();
                    yield return null;
                }
                if (gate != null) gate.transform.position = at + Vector3.down * 6f * (opens ? 0f : 1f);
                if (!opens && gate != null) { gates.Remove(gate); Object.Destroy(gate); }
            }

            /// <summary>
            /// The foremost Shadowlord traces a pattern in the air: the black arrow solidifies before
            /// it out of the dark, hangs a moment, and flies at <paramref name="slot"/>'s chest, where it stays.
            /// </summary>
            IEnumerator Bolt(int slot, float seconds)
            {
                var target = slots[slot];
                var from = slots.Where(a => a?.Go != null && a.Who == "shadowlord").OrderBy(a => target?.Go == null ? 0 : (a.Pos - target.Pos).sqrMagnitude).FirstOrDefault();
                if (target?.Go == null || from == null) { yield return Wait(seconds); yield break; }
                from.Go.transform.rotation = Stage.Face(from.Pos, target.Pos);
                var towards = (target.Pos - from.Pos).normalized;
                var start = from.Pos + Vector3.up * 1.5f + towards * 0.9f;
                var end = target.Pos + Vector3.up * 1.25f;
                var arrow = Arrow();
                arrow.transform.position = start;
                arrow.transform.rotation = Quaternion.LookRotation(end - start);
                // out of a swirl of darkness, growing
                var swirl = Particles("u5_intro_arrow_dark", start, new Color(0.04f, 0.02f, 0.07f, 0.9f), 0.35f, 0.9f, 50f, 0.35f, 4f, 0f);
                float t0 = Now, form = 1.6f;
                while (Now < t0 + form && Here && !Skipped)
                {
                    float f = (Now - t0) / form;
                    arrow.transform.localScale = Vector3.one * Mathf.SmoothStep(0.05f, 1f, f);
                    arrow.transform.position = start + Vector3.up * 0.05f * Mathf.Sin(Now * 6f);
                    Tick();
                    yield return null;
                }
                StopParticles(swirl);
                arrow.transform.localScale = Vector3.one;
                yield return Wait(0.6f);
                // with a flick of its hand
                Stage.Pose(from.Go, "point");
                t0 = Now;
                float flight = 0.3f;
                while (Now < t0 + flight && Here && !Skipped)
                {
                    arrow.transform.position = Vector3.Lerp(start, end, (Now - t0) / flight);
                    Tick();
                    yield return null;
                }
                arrow.transform.position = end;
                stuck = arrow;
                Stage.Effect("vfx_HitSparks", end, root.transform);
                Stage.Thunder(end, 0.6f);
                yield return Wait(seconds);
            }

            void Knock(Vector3 door)
            {
                var fx = ZNetScene.instance.GetPrefab("wood_door")?.GetComponent<Door>();
                if (fx != null) fx.m_openEffects.Create(door + Vector3.up, Quaternion.identity);
            }

            // ── the words ─────────────────────────────────────────────────────────────────────

            void BuildUi()
            {
                var gui = GUIManager.CustomGUIFront;
                var g = GUIManager.Instance;
                if (gui == null || g == null) return;
                ui = new GameObject("U5IntroStage", typeof(RectTransform));
                ui.transform.SetParent(gui.transform, false);
                var rt = ui.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;

                var band = new GameObject("words", typeof(RectTransform));
                band.transform.SetParent(ui.transform, false);
                var brt = band.GetComponent<RectTransform>();
                brt.anchorMin = new Vector2(0.5f, 0f); brt.anchorMax = new Vector2(0.5f, 0f); brt.pivot = new Vector2(0.5f, 0f);
                brt.sizeDelta = new Vector2(1180, 120); brt.anchoredPosition = new Vector2(0, 30);
                band.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
                words = g.CreateText("", band.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, g.AveriaSerifBold, 17, new Color(0.93f, 0.9f, 0.82f), true, Color.black, 1140, 100, false).GetComponent<Text>();
                words.alignment = TextAnchor.MiddleLeft;
                words.verticalOverflow = VerticalWrapMode.Overflow;
                band.SetActive(false);

                title = g.CreateText("", ui.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -110), g.Norse, 54, g.ValheimOrange, true, Color.black, 1000, 80, false).GetComponent<Text>();
                title.alignment = TextAnchor.MiddleCenter;
                hint = g.CreateText("Space: on      Escape: skip", ui.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-150, 12), g.AveriaSerifBold, 14, new Color(0.7f, 0.7f, 0.7f), true, Color.black, 280, 22, false).GetComponent<Text>();
                hint.alignment = TextAnchor.MiddleRight;

                var fade = new GameObject("fade", typeof(RectTransform));
                fade.transform.SetParent(ui.transform, false);
                var frt = fade.GetComponent<RectTransform>();
                frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one; frt.offsetMin = frt.offsetMax = Vector2.zero;
                fader = fade.AddComponent<Image>();
                fader.color = Color.black;
                fader.raycastTarget = false;
            }

            void ShowTitle(string text)
            {
                if (title == null) return;
                title.text = text;
                title.color = new Color(title.color.r, title.color.g, title.color.b, 1f);
                titleUntil = Now + 6f;
            }

            void SetFade(float a)
            {
                if (fader != null) fader.color = new Color(0f, 0f, 0f, a);
            }

            IEnumerator Fade(float to, float seconds)
            {
                float from = fader != null ? fader.color.a : 0f, start = Now;
                while (Now < start + seconds && Here && !Skipped)
                {
                    SetFade(Mathf.Lerp(from, to, (Now - start) / seconds));
                    Tick();
                    yield return null;
                }
                SetFade(to);
            }

            public void Clean()
            {
                ClearSet();
                if (slab != null) Object.Destroy(slab);
                if (ui != null) Object.Destroy(ui);
                Stage.Darkness(false);
                Stage.Held = false;
                Stage.Fov = 0f;
                Stage.SafeSpot = null;
                if (Hud.instance != null) Hud.instance.m_userHidden = hudWas;
                if (me != null && !me.IsDead() && me == Player.m_localPlayer) me.TeleportTo(home, homeFacing, false);
            }
        }
    }
}
