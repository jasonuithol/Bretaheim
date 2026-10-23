using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Bretaheim.Data;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Bretaheim.WorldGen
{
    public static partial class IntroStage
    {
        /// <summary>The story's pictures (crops of its screens), made once: as they are, and as visions (dark parts and edges see-through).</summary>
        static readonly Dictionary<int, Texture2D> arts = new Dictionary<int, Texture2D>();
        static Texture2D soft, codexSign;

        /// <summary>Which picture a paragraph of Iolo's tale shows in the hearth's smoke (his own screens are all his portrait).</summary>
        static readonly Dictionary<int, int> TaleArt = new Dictionary<int, int> { { 16, 0 }, { 17, 2 }, { 18, 11 }, { 19, 10 }, { 20, 9 } };

        /// <summary>
        /// What the Introduction tells that the View's tiles do not show: the clouds over the moon,
        /// the Codex forming in the circle, the amulet falling, the bedside light, the Ankh in its
        /// glass case, the amulet and Ankh glowing within the stones, the lightning strike, the
        /// Shadowlords out of swirling darkness, Shamino's sword, the arrow pulled out and hurled,
        /// the amulet's flare, the hut's warm windows, Iolo's mugs, and the story's own pictures:
        /// paintings on the bedroom wall, visions in the dark of the woods, pictures in the hearth's smoke.
        /// </summary>
        partial class Director
        {
            Light windowLight;
            bool ankhHeld, amuletHeld;
            GameObject heldAnkh, heldAmulet, caseAnkh, caseGlass, bedAmulet, groundAnkh, groundAmulet, stuck, droppedSword, candle;
            int caseStep;
            ParticleSystem shimmer;
            Vector3 glowAt;
            Vector3? hearthAt;
            readonly List<(Light light, float strength, float seed)> flickers = new List<(Light, float, float)>();
            readonly List<(Light light, Vector3 over, float seed)> beams = new List<(Light, Vector3, float)>();
            Renderer painting;
            Renderer vision, visionOld;
            RawImage seen, seenOld;
            float visionSince;

            // ── when ──────────────────────────────────────────────────────────────────────────

            /// <summary>What happens before a step of the script (<paramref name="instead"/>: in place of it).</summary>
            IEnumerator Before(ViewEvent ev, Action instead)
            {
                if (scene == 1 && ev.At == 0x2e0) { instead(); return Offering(Frames(ev.Frames)); }
                if (scene == 2 && ev.At == 0x330) { instead(); return Retrieve(Frames(ev.Frames)); }
                if (scene == 2 && ev.At == 0x33e) DrawSword(slots[1]);
                if (scene == 2 && ev.At == 0x384) return Flare();
                if (scene == 2 && ev.At == 0x3a5) { instead(); return Hurl(Frames(ev.Frames)); }
                return null;
            }

            /// <summary>What happens after a step of the script.</summary>
            IEnumerator After(ViewEvent ev)
            {
                var a = ev.Slot >= 0 ? slots[ev.Slot] : null;
                switch (scene)
                {
                    case 0:
                        if (ev.Kind == ViewEventKind.DissolveIn && a?.Go != null && a.Who == "glow")
                        {
                            glowAt = Centre(a.Go);
                            Plugin.Instance.StartCoroutine(Codex(a));
                        }
                        if (ev.At == 0x215) return AmuletFalls();
                        if (ev.At == 0x219) CandleOn();
                        break;
                    case 1:
                        if (ev.At == 0x2ea) Through();
                        break;
                    case 2:
                        if (ev.At == 0x31f) Plugin.Instance.StartCoroutine(Strike(Cell(14, 3) + Vector3.back * 1.5f));
                        if (ev.At == 0x329) AtFeet();
                        if (ev.At == 0x372) ShaminoFalls(a);
                        if (ev.At == 0x3a9) DrawSword(a);
                        break;
                    case 3:
                        if (ev.Kind == ViewEventKind.Place && a?.Who == "avatar_seated") Mugs();
                        break;
                }
                return null;
            }

            /// <summary>The scene's own things, once it is built.</summary>
            void SetDressing(int n)
            {
                // the Avatar carries the Ankh and the amulet but where they lie on the ground
                ankhHeld = amuletHeld = n != 2;
                if (n == 0)
                {
                    Plugin.Instance.StartCoroutine(Clouds());
                    GlassCase(Cell(15, 1));
                    // pictures on the wall: the one told now, and an old friend
                    painting = Painting(WallAt(5, 6), 0, 1.15f);
                    Painting(WallAt(10, 11), 9, 1.0f);
                    var bed = BedAt();
                    var top = Top("table", bed) ?? Top("chest", bed) ?? bed + new Vector3(1.2f, -0.5f, 0f);
                    var prefab = ZNetScene.instance.GetPrefab("Candle_resin");
                    candle = prefab != null ? Stage.Still(prefab, top, Quaternion.identity, 1f, 1f, root.transform) : new GameObject("u5_intro_candle");
                    candle.transform.SetParent(root.transform, true);
                    candle.transform.position = top;
                }
                if (n == 1)
                {
                    // moonbeams piercing the cloud cover, wandering over the woods
                    for (int i = 0; i < 3; i++)
                    {
                        var beam = Stage.Spot(Cell(4 + i * 5, 1) + Vector3.up * 30f, Cell(4 + i * 5, 1), new Color(0.6f, 0.7f, 1f), 2.2f, 45f, 14f, root.transform).GetComponent<Light>();
                        beam.name = "u5_intro_moonbeam";
                        beams.Add((beam, Cell(4 + i * 5, 1), i * 2.1f));
                    }
                }
                if (n == 3)
                {
                    if (hearthAt != null) Flicker(Stage.Glow(hearthAt.Value + Vector3.up * 1f, new Color(1f, 0.55f, 0.25f), 2f, 9f, root.transform), 2f);
                    // the hut's dim light through its windows and door, the hearth's smoke
                    Flicker(Stage.Glow(Cell(8, 3) + Vector3.up * 1.6f, new Color(1f, 0.6f, 0.3f), 1.4f, 9f, root.transform), 1.4f);
                    Flicker(Stage.Glow(Cell(5, 3) + Vector3.up * 1.4f, new Color(1f, 0.55f, 0.25f), 0.9f, 6f, root.transform), 0.9f);
                    Flicker(Stage.Glow(Cell(12, 3) + Vector3.up * 1.4f, new Color(1f, 0.55f, 0.25f), 0.9f, 6f, root.transform), 0.9f);
                    if (hearthAt != null)
                        Particles("u5_intro_hearth_smoke", hearthAt.Value + Vector3.up * 1.2f, new Color(0.32f, 0.3f, 0.29f, 0.3f), 0.9f, 4f, 5f, 0.25f, 0.3f, 0.55f);
                }
            }

            /// <summary>A piece of the set worth knowing where it is.</summary>
            void Remember(string name, GameObject go)
            {
                if (name == "fire_pit" || name == "hearth") hearthAt = go.transform.position;
            }

            void ClearEffects()
            {
                windowLight = null;
                heldAnkh = heldAmulet = caseAnkh = caseGlass = bedAmulet = groundAnkh = groundAmulet = stuck = droppedSword = candle = null;
                caseStep = 0;
                shimmer = null;
                hearthAt = null;
                flickers.Clear();
                beams.Clear();
                painting = vision = visionOld = null;
                foreach (var r in new[] { seen, seenOld }) if (r != null) Object.Destroy(r.gameObject);
                seen = seenOld = null;
            }

            /// <summary>Every frame: lights flicker, the visions find their place and fade.</summary>
            void TickEffects()
            {
                foreach (var (light, strength, seed) in flickers)
                    if (light != null) light.intensity = strength * (0.8f + 0.4f * Mathf.PerlinNoise(Now * 2.5f + seed, seed));
                foreach (var (beam, over, seed) in beams)
                {
                    if (beam == null) continue;
                    // (drifting, and now and then lost behind a cloud)
                    var to = over + new Vector3(Mathf.Sin(Now * 0.11f + seed) * 9f, 0f, Mathf.Cos(Now * 0.07f + seed) * 5f);
                    beam.transform.rotation = Quaternion.LookRotation(to - beam.transform.position);
                    beam.intensity = 2.2f * Mathf.Clamp01(Mathf.PerlinNoise(Now * 0.25f + seed, 7f) * 2f - 0.4f);
                }
                float f = Mathf.Clamp01((Now - visionSince) / 2f);
                if (seen != null) seen.color = new Color(1f, 1f, 1f, 0.2f * f);
                if (seenOld != null)
                {
                    seenOld.color = new Color(1f, 1f, 1f, 0.2f * (1f - f));
                    if (f >= 1f) { Object.Destroy(seenOld.gameObject); seenOld = null; }
                }
                if (vision == null) return;
                float most = scene == 3 ? 0.7f : 0.4f;
                Alpha(vision, most * f);
                if (visionOld != null)
                {
                    Alpha(visionOld, most * (1f - f));
                    if (f >= 1f) { Object.Destroy(visionOld.gameObject); visionOld = null; }
                }
                foreach (var r in new[] { vision, visionOld }) if (r != null) PlaceVision(r);
            }

            // ── the Summoning ─────────────────────────────────────────────────────────────────

            /// <summary>Clouds fill the sky and block the moon's pale light: the window's light dims, in wisps.</summary>
            IEnumerator Clouds()
            {
                var w = windowLight;
                if (w == null) yield break;
                float t0 = Now, span = 14f, from = w.intensity, moonFrom = moon != null ? moon.intensity : 0f;
                while (w != null && scene == 0 && Now < t0 + span)
                {
                    float f = (Now - t0) / span;
                    w.intensity = Mathf.Lerp(from, from * 0.12f, f) * (0.7f + 0.3f * Mathf.PerlinNoise(Now * 1.4f, 3f));
                    if (moon != null) moon.intensity = Mathf.Lerp(moonFrom, moonFrom * 0.4f, f);
                    yield return null;
                }
            }

            /// <summary>A darkening in the circle's centre becomes the symbol of the Codex.</summary>
            IEnumerator Codex(Actor glow)
            {
                if (glow?.Go == null) yield break;
                var dark = Quad("u5_intro_codex_dark", Soft(), new Color(0f, 0f, 0.04f, 0f), 0.7f);
                var sign = Quad("u5_intro_codex", CodexSign(), new Color(0.75f, 0.88f, 1f, 0f), 0.55f);
                float t0 = Now, span = Frames(30) + 1f;
                float full = glow.Go.transform.localScale.x;
                while (glow.Go != null && root != null)
                {
                    float f = (Now - t0) / span, k = full > 0 ? glow.Go.transform.localScale.x / full : 1f;
                    var at = Centre(glow.Go);
                    foreach (var q in new[] { dark, sign })
                    {
                        q.transform.position = at + (camAt - at).normalized * 0.05f * (q == sign ? 2 : 1);
                        q.transform.rotation = Quaternion.LookRotation(at - camAt);
                    }
                    dark.transform.localScale = Vector3.one * 0.7f * k;
                    sign.transform.localScale = Vector3.one * 0.55f * k;
                    Alpha(dark, Mathf.Clamp01(f * 2f) * 0.9f);
                    Alpha(sign, Mathf.Clamp01(f * 2f - 0.7f));
                    yield return null;
                }
                if (dark != null) Object.Destroy(dark.gameObject);
                if (sign != null) Object.Destroy(sign.gameObject);
            }

            /// <summary>The light winks out and something falls softly onto the bed: the amulet.</summary>
            IEnumerator AmuletFalls()
            {
                var bed = BedAt() + new Vector3(0.45f, -0.2f, -0.2f);
                var from = glowAt == Vector3.zero ? bed + Vector3.up * 1.5f : glowAt;
                bedAmulet = Amulet(from, root.transform);
                float t0 = Now, fall = 0.55f;
                while (Now < t0 + fall && bedAmulet != null && Here && !Skipped)
                {
                    float f = (Now - t0) / fall;
                    bedAmulet.transform.position = Vector3.Lerp(from, bed, f * f);
                    Tick();
                    yield return null;
                }
                if (bedAmulet != null) bedAmulet.transform.position = bed;
            }

            /// <summary>Reaching out, the light at the bedside turned on; the amulet taken up.</summary>
            void CandleOn()
            {
                if (candle != null)
                {
                    var flame = candle.transform.position + Vector3.up * 0.32f;
                    foreach (Transform t in candle.GetComponentsInChildren<Transform>(true))
                        if (t.name.IndexOf("enabled", StringComparison.OrdinalIgnoreCase) >= 0 || t.name.IndexOf("flame", StringComparison.OrdinalIgnoreCase) >= 0) t.gameObject.SetActive(true);
                    Flicker(Stage.Glow(flame + Vector3.up * 0.2f, new Color(1f, 0.7f, 0.4f), 1.6f, 7f, root.transform), 1.6f);
                    Particles("u5_intro_candle_flame", flame, new Color(1f, 0.75f, 0.35f, 0.9f), 0.07f, 0.35f, 30f, 0.01f, 0f, 0.35f);
                    Stage.Effect("fx_candle_on", flame, root.transform);
                }
                if (bedAmulet != null) Object.Destroy(bedAmulet);
                bedAmulet = null;
                amuletHeld = true;
            }

            /// <summary>The Ankh on its velvet, under glass, on whatever stands in its cell (or a stand of its own).</summary>
            void GlassCase(Vector3 at)
            {
                at += Vector3.forward * 0.4f;
                var top = byCell.TryGetValue(MapCell(15, 1), out var list) && list.Count > 0 ? Top(list) : (Vector3?)null;
                if (top == null)
                {
                    Block("u5_intro_stand", at + Vector3.up * 0.45f, new Vector3(0.6f, 0.9f, 0.45f), Lit(new Color(0.25f, 0.16f, 0.09f)));
                    top = at + Vector3.up * 0.9f;
                }
                var t = top.Value;
                Block("u5_intro_velvet", t + Vector3.up * 0.02f, new Vector3(0.5f, 0.04f, 0.34f), Lit(new Color(0.45f, 0.04f, 0.06f)));
                caseAnkh = Ankh(0.34f, root.transform);
                caseAnkh.transform.position = t + Vector3.up * 0.05f;
                caseAnkh.transform.rotation = Quaternion.Euler(-15f, 0f, 0f);
                Stage.Glow(t + Vector3.up * 0.3f + Vector3.back * 0.3f, new Color(1f, 0.85f, 0.45f), 0.8f, 1.6f, caseAnkh.transform);
                caseGlass = Block("u5_intro_glass", t + Vector3.up * 0.23f, new Vector3(0.54f, 0.42f, 0.38f), Unlit(new Color(0.7f, 0.85f, 1f, 0.16f)));
            }

            /// <summary>The glass case opened (first), then the Ankh taken out (second).</summary>
            IEnumerator OpenCase()
            {
                int step = ++caseStep;
                if (step == 1)
                {
                    var glass = caseGlass;
                    if (glass == null) yield break;
                    var from = glass.transform.position;
                    for (float t0 = Now; glass != null && Now < t0 + 1.2f;)
                    {
                        float f = (Now - t0) / 1.2f;
                        glass.transform.position = from + Vector3.up * 0.45f * f;
                        Alpha(glass.GetComponent<Renderer>(), 0.16f * (1f - f));
                        yield return null;
                    }
                    if (glass != null) Object.Destroy(glass);
                    yield break;
                }
                var ankh = caseAnkh;
                var avatar = slots[0];
                if (ankh == null) yield break;
                Stage.Effect("vfx_odin_despawn", ankh.transform.position + Vector3.up * 0.2f, root.transform);
                var start = ankh.transform.position;
                for (float t0 = Now; ankh != null && Now < t0 + 1f;)
                {
                    float f = (Now - t0) / 1f;
                    var to = avatar?.Go != null ? (Hand(avatar.Go, true)?.position ?? avatar.Pos + Vector3.up * 1.2f) : start + Vector3.up * 0.5f;
                    ankh.transform.position = Vector3.Lerp(start + Vector3.up * 0.3f * Mathf.Sin(f * Mathf.PI), to, f * f);
                    yield return null;
                }
                if (ankh != null) Object.Destroy(ankh);
                caseAnkh = null;
                ankhHeld = true;
                if (avatar?.Go != null) Carry(avatar);
            }

            // ── the Journey ───────────────────────────────────────────────────────────────────

            /// <summary>
            /// Before the circle of stones: minutes slip by; the amulet slips from the Avatar's grip
            /// into the circle; a bluish shimmering above it, the Ankh glowing blue too; the Avatar
            /// kneels and sets the Ankh beside the amulet.
            /// </summary>
            IEnumerator Offering(float seconds)
            {
                float t0 = Now;
                var av = slots[0];
                var spot = Cell(13, 1);
                if (av?.Go == null) { yield return Wait(seconds); yield break; }
                av.Go.transform.rotation = Stage.Face(av.Pos, spot);
                yield return Wait(1.5f);
                // the amulet slips from the grip
                groundAmulet = heldAmulet != null ? heldAmulet : Amulet(av.Pos + Vector3.up * 1f, root.transform);
                heldAmulet = null;
                amuletHeld = false;
                groundAmulet.transform.SetParent(root.transform, true);
                var from = groundAmulet.transform.position;
                for (float f0 = Now; Now < f0 + 0.5f && Here && !Skipped;)
                {
                    float f = (Now - f0) / 0.5f;
                    groundAmulet.transform.position = Vector3.Lerp(from, spot + Vector3.up * 0.05f, f * f);
                    Tick();
                    yield return null;
                }
                groundAmulet.transform.position = spot + Vector3.up * 0.05f;
                yield return Wait(1.2f);
                // a bluish shimmering in the air above it, and about the Ankh
                var blue = new Color(0.45f, 0.65f, 1f);
                shimmer = Particles("u5_intro_shimmer", spot + Vector3.up * 0.7f, new Color(blue.r, blue.g, blue.b, 0.85f), 0.12f, 1.4f, 30f, 0.4f, 1.5f, 0.35f);
                var glow = Stage.Glow(spot + Vector3.up * 0.7f, blue, 0f, 5f, root.transform).GetComponent<Light>();
                Flicker(glow, 1.6f);
                if (heldAnkh != null)
                {
                    Stage.Glow(heldAnkh.transform.position, blue, 1.2f, 2.5f, heldAnkh.transform);
                    Tint(heldAnkh, new Color(0.55f, 0.75f, 1f));
                    Particles("u5_intro_ankh_glow", heldAnkh.transform.position, new Color(blue.r, blue.g, blue.b, 0.7f), 0.08f, 0.8f, 18f, 0.15f, 1f, 0.2f, 0.1f, heldAnkh.transform);
                }
                yield return Wait(1.8f);
                // carefully, the glowing Ankh beside the amulet
                Stage.Pose(av.Go, "kneel");
                yield return Wait(1.2f);
                if (heldAnkh != null)
                {
                    groundAnkh = heldAnkh;
                    heldAnkh = null;
                    groundAnkh.transform.SetParent(root.transform, true);
                    groundAnkh.transform.position = spot + Vector3.right * 0.45f;
                    groundAnkh.transform.rotation = Quaternion.identity;
                }
                ankhHeld = false;
                yield return Wait(Mathf.Max(0.8f, seconds - (Now - t0)));
                av.Anim?.SetTrigger("emote_stop");
            }

            /// <summary>Through the door: the Ankh and amulet go with the Avatar (to lie at their feet on arrival).</summary>
            void Through()
            {
                foreach (var go in new[] { groundAnkh, groundAmulet }) if (go != null) Object.Destroy(go);
                groundAnkh = groundAmulet = null;
                if (shimmer != null) StopParticles(shimmer);
                shimmer = null;
            }

            // ── the Arrival ───────────────────────────────────────────────────────────────────

            /// <summary>A blinding streak of lightning strikes with a thunderous crash not a hundred feet away: a bolt, a flash, a scorch, smoke.</summary>
            IEnumerator Strike(Vector3 at)
            {
                if (root == null) yield break;
                var go = new GameObject("u5_intro_strike");
                go.transform.SetParent(root.transform, false);
                var line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.positionCount = 18;
                line.widthMultiplier = 0.7f;
                line.widthCurve = AnimationCurve.Linear(0f, 0.5f, 1f, 1f);
                line.material = Unlit(new Color(0.88f, 0.92f, 1f));
                var flash = Stage.Glow(at + Vector3.up * 4f, new Color(0.85f, 0.9f, 1f), 4f, 40f, root.transform);
                Stage.Thunder(at, 1f);
                float was = moon != null ? moon.intensity : 0f;
                for (int pulse = 0; pulse < 3 && go != null; pulse++)
                {
                    for (int i = 0; i < line.positionCount; i++)
                    {
                        float f = i / (line.positionCount - 1f);
                        var jag = i == 0 || i == line.positionCount - 1 ? Vector3.zero : new Vector3(UnityEngine.Random.Range(-1.6f, 1.6f), 0f, UnityEngine.Random.Range(-1.2f, 1.2f));
                        line.SetPosition(i, at + Vector3.up * 50f * (1f - f) + jag);
                    }
                    line.enabled = true;
                    if (flash != null) flash.SetActive(true);
                    if (moon != null) moon.intensity = 1.1f;
                    yield return new WaitForSeconds(pulse == 1 ? 0.12f : 0.07f);
                    if (go == null) yield break;
                    line.enabled = false;
                    if (flash != null) flash.SetActive(false);
                    if (moon != null) moon.intensity = was;
                    yield return new WaitForSeconds(0.06f);
                }
                // its afterimage fades
                if (go != null) line.enabled = true;
                for (float t0 = Time.time; go != null && Time.time < t0 + 0.5f;)
                {
                    var c = new Color(0.88f, 0.92f, 1f, 1f - (Time.time - t0) / 0.5f);
                    line.startColor = line.endColor = c;
                    yield return null;
                }
                if (go != null) Object.Destroy(go);
                if (flash != null) Object.Destroy(flash);
                if (root == null) yield break;
                Stage.Effect("fx_chainlightning_hit", at, root.transform);
                StopParticles(Particles("u5_intro_strike_burst", at + Vector3.up * 0.3f, new Color(0.85f, 0.9f, 1f, 1f), 0.15f, 0.5f, 400f, 0.3f, 0f, 0.5f, 6f), 0.15f);
                var scorch = Quad("u5_intro_scorch", Soft(), new Color(0f, 0f, 0f, 0.8f), 3f);
                scorch.transform.position = at + Vector3.up * 0.04f;
                scorch.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                var smoke = Particles("u5_intro_strike_smoke", at + Vector3.up * 0.4f, new Color(0.2f, 0.2f, 0.22f, 0.5f), 0.8f, 3.5f, 8f, 0.6f, 0.2f, 0.8f);
                var embers = Particles("u5_intro_strike_embers", at + Vector3.up * 0.2f, new Color(1f, 0.55f, 0.2f, 1f), 0.06f, 1.2f, 25f, 0.6f, 0f, 1.2f, 0.8f);
                StopParticles(embers, 2.5f);
                StopParticles(smoke, 12f);
            }

            /// <summary>The amulet and Ankh lie at the Avatar's feet, come through with them.</summary>
            void AtFeet()
            {
                var av = slots[0];
                var at = av?.Go != null ? av.Pos : Cell(9, 2);
                groundAnkh = Ankh(0.34f, root.transform);
                groundAnkh.transform.position = at + new Vector3(0.5f, 0.05f, -0.4f);
                groundAnkh.transform.rotation = Quaternion.Euler(90f, 30f, 0f);
                groundAmulet = Amulet(at + new Vector3(-0.4f, 0.05f, -0.3f), root.transform);
            }

            /// <summary>Hastily the Avatar takes up the amulet and Ankh at their feet (kneeling a moment).</summary>
            IEnumerator Retrieve(float seconds)
            {
                float t0 = Now;
                var av = slots[0];
                if (av?.Go != null && (groundAnkh != null || groundAmulet != null))
                {
                    Stage.Pose(av.Go, "kneel");
                    yield return Wait(1.3f);
                    foreach (var go in new[] { groundAnkh, groundAmulet }) if (go != null) Object.Destroy(go);
                    groundAnkh = groundAmulet = null;
                    ankhHeld = amuletHeld = true;
                    Carry(av);
                    yield return Wait(0.6f);
                    av.Anim?.SetTrigger("emote_stop");
                }
                yield return Wait(Mathf.Max(0f, seconds - (Now - t0)));
            }

            /// <summary>Shamino draws his sword (and has it again once he stands).</summary>
            void DrawSword(Actor shamino)
            {
                if (droppedSword != null) { Object.Destroy(droppedSword); droppedSword = null; }
                var vis = shamino?.Go != null ? shamino.Go.GetComponent<VisEquipment>() : null;
                if (vis != null) Stage.Dress(vis, new[] { "SwordIron" });
            }

            /// <summary>Shamino down: his sword dropped beside him, the black arrow in his chest.</summary>
            void ShaminoFalls(Actor down)
            {
                if (down?.Go == null) return;
                var prefab = ZNetScene.instance.GetPrefab("SwordIron");
                if (prefab != null) droppedSword = Stage.Still(prefab, down.Pos + new Vector3(0.8f, 0.08f, -0.5f), Quaternion.Euler(0f, 35f, 90f), 1f, 1f, root.transform);
                if (stuck != null)
                {
                    var dir = stuck.transform.forward;
                    stuck.transform.position = down.Pos + Vector3.up * 0.55f;
                    stuck.transform.rotation = Quaternion.LookRotation(Vector3.down * 0.8f + new Vector3(dir.x, 0f, dir.z).normalized * 0.6f);
                }
            }

            /// <summary>The amulet emits a bright blue glow: the Shadowlords shrink back.</summary>
            IEnumerator Flare()
            {
                var av = slots[0];
                if (av?.Go == null) yield break;
                var at = heldAmulet != null ? heldAmulet.transform.position : av.Pos + Vector3.up * 1.2f;
                var blue = new Color(0.5f, 0.72f, 1f);
                var light = Stage.Glow(at, blue, 0f, 16f, root.transform).GetComponent<Light>();
                var burst = Particles("u5_intro_flare", at, new Color(blue.r, blue.g, blue.b, 0.9f), 0.18f, 1f, 160f, 0.2f, 3f, 0.2f, 2.5f);
                Stage.Effect("vfx_odin_despawn", at, root.transform);
                for (float t0 = Now; Now < t0 + 0.4f && Here && !Skipped;)
                {
                    if (light != null) light.intensity = 8f * (Now - t0) / 0.4f;
                    Tick();
                    yield return null;
                }
                StopParticles(burst, 0.5f);
                yield return Wait(0.5f);
                Plugin.Instance.StartCoroutine(Fades(light, 3f));
            }

            IEnumerator Fades(Light light, float seconds)
            {
                if (light == null) yield break;
                float from = light.intensity;
                for (float t0 = Now; light != null && Now < t0 + seconds;)
                {
                    light.intensity = Mathf.Lerp(from, 0.8f, (Now - t0) / seconds);
                    yield return null;
                }
                if (light != null) Object.Destroy(light.gameObject);
            }

            /// <summary>The Avatar jerks the arrow from Shamino and hurls it into the darkness, the air about it crackling.</summary>
            IEnumerator Hurl(float seconds)
            {
                float t0 = Now;
                var av = slots[0];
                var arrow = stuck;
                if (arrow == null || av?.Go == null) { yield return Wait(seconds); yield break; }
                stuck = null;
                av.Go.transform.rotation = Stage.Face(av.Pos, arrow.transform.position);
                var from = arrow.transform.position;
                var hand = av.Pos + Vector3.up * 1.6f + av.Go.transform.forward * 0.2f;
                for (float f0 = Now; Now < f0 + 0.35f && Here && !Skipped;)
                {
                    arrow.transform.position = Vector3.Lerp(from, hand, (Now - f0) / 0.35f);
                    Tick();
                    yield return null;
                }
                Stage.Effect("vfx_HitSparks", from, root.transform);
                yield return Wait(0.5f);
                var far = av.Pos + new Vector3(-12f, 4f, 15f);
                av.Go.transform.rotation = Stage.Face(av.Pos, far);
                Stage.Pose(av.Go, "point");
                var trail = Particles("u5_intro_arrow_trail", hand, new Color(0.6f, 0.3f, 1f, 0.8f), 0.12f, 0.6f, 60f, 0.1f, 0f, 0f, 0.3f, arrow.transform);
                Stage.Thunder(hand, 0.3f);
                var crackle = Stage.Glow(hand, new Color(0.65f, 0.4f, 1f), 2f, 5f, arrow.transform).GetComponent<Light>();
                Flicker(crackle, 2.5f);
                float flight = 1.4f, spark = 0f;
                var last = hand;
                for (float f0 = Now; Now < f0 + flight && Here && !Skipped && arrow != null;)
                {
                    float f = (Now - f0) / flight;
                    var pos = Vector3.Lerp(hand, far, f) + Vector3.up * Mathf.Sin(f * Mathf.PI) * 3f;
                    if ((pos - last).sqrMagnitude > 0.0001f) arrow.transform.rotation = Quaternion.LookRotation(pos - last);
                    arrow.transform.position = last = pos;
                    if (Now > spark) { spark = Now + 0.15f; Stage.Effect("vfx_HitSparks", pos, root.transform); }
                    Tick();
                    yield return null;
                }
                if (trail != null) { trail.transform.SetParent(root.transform, true); StopParticles(trail); }
                if (arrow != null) Object.Destroy(arrow);
                yield return Wait(Mathf.Max(0f, seconds - (Now - t0)));
            }

            // ── the Welcoming ─────────────────────────────────────────────────────────────────

            /// <summary>Iolo fills two mugs, steaming, on the table by the hearth.</summary>
            void Mugs()
            {
                var top = Top("table", Cell(10, 2));
                if (top == null) return;
                var prefab = ZNetScene.instance.GetPrefab("Tankard") ?? ZNetScene.instance.GetPrefab("prop_Tankard");
                foreach (var dx in new[] { -0.28f, 0.3f })
                {
                    var at = top.Value + new Vector3(dx, 0f, dx * 0.4f);
                    if (prefab != null)
                    {
                        var mug = Stage.Still(prefab, at, Quaternion.Euler(0f, dx * 300f, 0f), 1f, 1f, root.transform);
                        var b = Bounds(mug);
                        if (b != null) mug.transform.position += Vector3.up * (at.y - b.Value.min.y);
                    }
                    Particles("u5_intro_steam", at + Vector3.up * 0.22f, new Color(0.9f, 0.9f, 0.9f, 0.22f), 0.09f, 1.6f, 6f, 0.04f, 0.4f, 0.35f);
                }
            }

            // ── the pictures ──────────────────────────────────────────────────────────────────

            /// <summary>The paragraph now told shown as the story's own picture: the painting in the bedroom, else a vision.</summary>
            void Illustrate(int p)
            {
                if (root == null || screens == null) return;
                if (scene == 0)
                {
                    var tex = Art(p, false);
                    if (painting != null && tex != null)
                    {
                        painting.material.mainTexture = tex;
                        var s = painting.transform.localScale;
                        painting.transform.localScale = new Vector3(s.x, s.x * tex.height / tex.width, 1f);
                    }
                    return;
                }
                int art = TaleArt.TryGetValue(p, out int other) ? other : p;
                var look = Art(art, true);
                if (look == null) return;
                visionSince = Now;
                if (scene == 3)
                {
                    if (visionOld != null) Object.Destroy(visionOld.gameObject);
                    visionOld = vision;
                    vision = Quad("u5_intro_vision", look, new Color(1f, 1f, 1f, 0f), 1f);
                    PlaceVision(vision);
                    return;
                }
                // out of doors: a memory over the dark of the view, as a film's double exposure
                if (ui == null) return;
                if (seenOld != null) Object.Destroy(seenOld.gameObject);
                seenOld = seen;
                var go = new GameObject("vision", typeof(RectTransform));
                go.transform.SetParent(ui.transform, false);
                go.transform.SetSiblingIndex(0);
                seen = go.AddComponent<RawImage>();
                seen.texture = look;
                seen.raycastTarget = false;
                seen.color = new Color(1f, 1f, 1f, 0f);
                var rt = seen.rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
                rt.anchoredPosition = new Vector2(0f, -30f);
                // (up at the top, out of the figures' way: no taller than a quarter of the screen)
                float h = Mathf.Min(ui.GetComponent<RectTransform>().rect.height * 0.27f, 260f), w = h * look.width / look.height;
                rt.sizeDelta = new Vector2(w, h);
            }

            /// <summary>A vision's place in the hut: above the hearth, in its smoke, facing the camera.</summary>
            void PlaceVision(Renderer r)
            {
                var tex = r.sharedMaterial.mainTexture;
                float aspect = tex != null ? (float)tex.height / tex.width : 0.7f;
                var at = (hearthAt ?? Cell(10, 1)) + Vector3.up * (2.1f + 0.15f * Mathf.Sin(Now * 0.6f));
                r.transform.position = at;
                r.transform.rotation = Quaternion.LookRotation(at - camAt);
                r.transform.localScale = new Vector3(2.2f, 2.2f * aspect, 1f);
            }

            /// <summary>A framed picture of the story on the wall at <paramref name="at"/> (its face), facing into the room.</summary>
            Renderer Painting(Vector3 at, int p, float width)
            {
                var tex = Art(p, false);
                if (tex == null) return null;
                float h = width * tex.height / tex.width;
                var inward = Inward();
                Block("u5_intro_frame", at - inward * 0.03f, new Vector3(width + 0.14f, h + 0.14f, 0.05f), Lit(new Color(0.2f, 0.12f, 0.06f)));
                var q = Quad("u5_intro_painting", tex, new Color(0.55f, 0.53f, 0.5f, 1f), 1f);
                q.transform.position = at + inward * 0.01f;
                q.transform.rotation = Quaternion.LookRotation(-inward);
                q.transform.localScale = new Vector3(width, h, 1f);
                return q;
            }

            /// <summary>The middle of the bedroom's north wall between two columns, on its inner face, at a picture's height.</summary>
            Vector3 WallAt(int c0, int c1)
            {
                var at = (Cell(c0, 0) + Cell(c1, 0)) / 2f;
                float into = Inward().z;
                float face = at.z + into * 0.5f;
                foreach (int c in new[] { c0, c1 })
                    if (byCell.TryGetValue(MapCell(c, 0), out var list))
                        foreach (var go in list)
                        {
                            var b = Bounds(go);
                            if (b != null && b.Value.size.y > 1f) face = into > 0 ? Mathf.Max(face, b.Value.max.z) : Mathf.Min(face, b.Value.min.z);
                        }
                return new Vector3(at.x, Origin.y + 1.9f, face + into * 0.03f);
            }

            /// <summary>From the strip's first row into the room (the way its north wall faces).</summary>
            Vector3 Inward() => new Vector3(0f, 0f, Mathf.Sign(Cell(0, 2).z - Cell(0, 0).z));

            /// <summary>A story screen's picture: as drawn (pixels kept), or as a vision (soft, its dark parts and edges see-through).</summary>
            Texture2D Art(int p, bool asVision)
            {
                if (screens == null || p < 0 || p >= screens.Count || screens[p].ArtW == 0) return null;
                int key = p * 2 + (asVision ? 1 : 0);
                if (arts.TryGetValue(key, out var made) && made != null) return made;
                var page = screens[p];
                int w = page.ArtW, h = page.ArtH;
                var rgba = page.Art.Rgba();
                var px = new Color32[w * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = ((page.ArtY + h - 1 - y) * Bretaheim.Data.Screen.W + page.ArtX + x) * 4;
                    byte r = rgba[i], g = rgba[i + 1], b = rgba[i + 2], a = 255;
                    if (asVision)
                    {
                        float ex = x / (w - 1f) * 2f - 1f, ey = y / (h - 1f) * 2f - 1f;
                        float edge = Mathf.Clamp01((1f - Mathf.Sqrt(ex * ex + ey * ey) * 0.95f) * 2.4f);
                        float lum = (0.3f * r + 0.59f * g + 0.11f * b) / 255f;
                        a = (byte)(255 * edge * Mathf.SmoothStep(0.04f, 0.45f, lum));
                        // a memory in moonlight: half its colours, half a cold blue
                        r = (byte)Mathf.Lerp(r, lum * 150f, 0.55f);
                        g = (byte)Mathf.Lerp(g, lum * 190f, 0.55f);
                        b = (byte)Mathf.Lerp(b, lum * 255f, 0.55f);
                    }
                    px[y * w + x] = new Color32(r, g, b, a);
                }
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = asVision ? FilterMode.Bilinear : FilterMode.Point, name = $"u5_story_art{key}" };
                tex.SetPixels32(px);
                tex.Apply(false);
                arts[key] = tex;
                return tex;
            }

            // ── the things ────────────────────────────────────────────────────────────────────

            /// <summary>The Ankh, in gold: a stem, a crossbar and a loop, <paramref name="height"/> m tall.</summary>
            GameObject Ankh(float height, Transform parent)
            {
                var go = new GameObject("u5_intro_ankh");
                go.transform.SetParent(parent, false);
                var gold = Lit(new Color(0.95f, 0.72f, 0.22f));
                void Bar(Vector3 at, Vector3 size, float roll)
                {
                    var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Object.Destroy(c.GetComponent<Collider>());
                    c.name = "u5_intro_ankh_part";
                    c.transform.SetParent(go.transform, false);
                    c.transform.localPosition = at;
                    c.transform.localRotation = Quaternion.Euler(0f, 0f, roll);
                    c.transform.localScale = size;
                    c.GetComponent<Renderer>().sharedMaterial = gold;
                }
                Bar(new Vector3(0f, 0.31f, 0f), new Vector3(0.09f, 0.62f, 0.06f), 0f);
                Bar(new Vector3(0f, 0.6f, 0f), new Vector3(0.5f, 0.08f, 0.06f), 0f);
                const int n = 12;
                for (int i = 0; i < n; i++)
                {
                    float a0 = i * Mathf.PI * 2f / n, a1 = (i + 1) * Mathf.PI * 2f / n;
                    var p0 = new Vector3(Mathf.Sin(a0) * 0.12f, 0.81f + Mathf.Cos(a0) * 0.17f, 0f);
                    var p1 = new Vector3(Mathf.Sin(a1) * 0.12f, 0.81f + Mathf.Cos(a1) * 0.17f, 0f);
                    var d = p1 - p0;
                    Bar((p0 + p1) / 2f, new Vector3(d.magnitude + 0.02f, 0.07f, 0.06f), Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
                }
                go.transform.localScale = Vector3.one * height;
                return go;
            }

            /// <summary>The amulet: a silver necklace with a faint blue gleam.</summary>
            GameObject Amulet(Vector3 at, Transform parent)
            {
                var prefab = ZNetScene.instance.GetPrefab("SilverNecklace");
                GameObject go;
                if (prefab != null) go = Stage.Still(prefab, at, Quaternion.identity, 1f, 1f, parent);
                else
                {
                    go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Object.Destroy(go.GetComponent<Collider>());
                    go.transform.SetParent(parent, false);
                    go.transform.position = at;
                    go.transform.localScale = Vector3.one * 0.08f;
                    go.GetComponent<Renderer>().sharedMaterial = Lit(new Color(0.75f, 0.8f, 0.9f));
                }
                go.name = "u5_intro_amulet";
                Stage.Glow(at + Vector3.up * 0.1f, new Color(0.45f, 0.6f, 1f), 0.5f, 1.2f, go.transform);
                return go;
            }

            /// <summary>The Ankh (right hand) and the amulet (left) in an Avatar's hands, while held.</summary>
            void Carry(Actor a)
            {
                if (a?.Go == null) return;
                if (ankhHeld)
                {
                    var hand = Hand(a.Go, true);
                    if (hand != null)
                    {
                        heldAnkh = Ankh(0.3f / Mathf.Max(0.01f, hand.lossyScale.y), hand);
                        heldAnkh.transform.localPosition = Vector3.zero;
                        heldAnkh.transform.rotation = a.Go.transform.rotation;
                    }
                }
                if (amuletHeld)
                {
                    var hand = Hand(a.Go, false);
                    if (hand != null)
                    {
                        heldAmulet = Amulet(hand.position, hand);
                        heldAmulet.transform.localScale = Vector3.one * 0.8f / Mathf.Max(0.01f, hand.lossyScale.y);
                    }
                }
            }

            static Transform Hand(GameObject figure, bool right)
            {
                var vis = figure != null ? figure.GetComponent<VisEquipment>() : null;
                return vis == null ? null : Traverse.Create(vis).Field<Transform>(right ? "m_rightHand" : "m_leftHand").Value;
            }

            /// <summary>The black arrow: a shaft, a head and fletching, with a violet glimmer.</summary>
            GameObject Arrow()
            {
                var go = new GameObject("u5_intro_arrow");
                go.transform.SetParent(root.transform, false);
                var black = Unlit(new Color(0.02f, 0.02f, 0.03f));
                foreach (var (part, at, size, yaw) in new[]
                {
                    ("shaft", Vector3.zero, new Vector3(0.04f, 0.04f, 0.85f), 0f),
                    ("head", new Vector3(0f, 0f, 0.47f), new Vector3(0.1f, 0.03f, 0.1f), 45f),
                    ("fletching", new Vector3(0f, 0f, -0.36f), new Vector3(0.14f, 0.01f, 0.16f), 0f),
                })
                {
                    var b = Block(part, Vector3.zero, size, black);
                    b.transform.SetParent(go.transform, false);
                    b.transform.localPosition = at;
                    b.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                }
                Stage.Glow(go.transform.position, new Color(0.6f, 0.2f, 1f), 2.5f, 4f, go.transform);
                return go;
            }

            void Smoke(Vector3 at, float seconds)
            {
                if (root == null) return;
                var ps = Particles("u5_intro_darkness", at + Vector3.up * 1f, new Color(0.03f, 0.02f, 0.05f, 0.85f), 1.1f, 1.8f, 45f, 0.7f, 3f, 0.4f);
                StopParticles(ps, seconds);
            }

            void DoorSound(Vector3 at, bool opens)
            {
                var door = ZNetScene.instance.GetPrefab("wood_door")?.GetComponent<Door>();
                if (door == null) return;
                (opens ? door.m_openEffects : door.m_closeEffects).Create(at + Vector3.up, Quaternion.identity);
            }

            void Flicker(GameObject go, float strength)
            {
                var l = go != null ? go.GetComponent<Light>() : null;
                if (l != null) Flicker(l, strength);
            }

            void Flicker(Light l, float strength) => flickers.Add((l, strength, UnityEngine.Random.Range(0f, 100f)));

            Vector3 BedAt()
            {
                var near = Cell(3, 3);
                var beds = seats.Values.Where(s => s.bed).Select(s => s.at).ToList();
                return beds.Count > 0 ? beds.OrderBy(b => (b - near).sqrMagnitude).First() : near + Vector3.up * 0.5f;
            }

            /// <summary>The top of the set's piece nearest <paramref name="near"/> whose name has <paramref name="part"/> in it.</summary>
            Vector3? Top(string part, Vector3 near)
            {
                if (root == null) return null;
                var go = root.transform.Cast<Transform>().Where(t => t.name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(t => (t.position - near).sqrMagnitude).FirstOrDefault();
                return go != null ? Top(new List<GameObject> { go.gameObject }) : null;
            }

            static Vector3? Top(List<GameObject> gos)
            {
                Bounds? all = null;
                foreach (var go in gos)
                {
                    var b = Bounds(go);
                    if (b == null) continue;
                    if (all == null) all = b;
                    else { var x = all.Value; x.Encapsulate(b.Value); all = x; }
                }
                return all == null ? (Vector3?)null : new Vector3(all.Value.center.x, all.Value.max.y, all.Value.center.z);
            }

            static Bounds? Bounds(GameObject go)
            {
                Bounds? all = null;
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    if (r is ParticleSystemRenderer) continue;
                    if (all == null) all = r.bounds;
                    else { var b = all.Value; b.Encapsulate(r.bounds); all = b; }
                }
                return all;
            }

            static Vector3 Centre(GameObject go) => go.transform.position + Vector3.up * 1.85f * go.transform.localScale.y;

            // ── making ────────────────────────────────────────────────────────────────────────

            GameObject Block(string name, Vector3 at, Vector3 size, Material material)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(c.GetComponent<Collider>());
                c.name = name;
                c.transform.SetParent(root.transform, false);
                c.transform.position = at;
                c.transform.localScale = size;
                c.GetComponent<Renderer>().sharedMaterial = material;
                return c;
            }

            Renderer Quad(string name, Texture texture, Color colour, float size)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.Destroy(q.GetComponent<Collider>());
                q.name = name;
                q.transform.SetParent(root.transform, false);
                q.transform.localScale = Vector3.one * size;
                var r = q.GetComponent<Renderer>();
                r.sharedMaterial = Unlit(colour, texture);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                return r;
            }

            static void Alpha(Renderer r, float a)
            {
                if (r == null) return;
                var c = r.sharedMaterial.color;
                r.sharedMaterial.color = new Color(c.r, c.g, c.b, a);
            }

            static void Tint(GameObject go, Color colour)
            {
                var m = Lit(colour);
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = m;
            }

            /// <summary>Drawn as it is, unlit (Unity's sprite shader: alpha blended, both sides).</summary>
            static Material Unlit(Color colour, Texture texture = null)
            {
                var m = new Material(Shader.Find("Sprites/Default")) { color = colour };
                if (texture != null) m.mainTexture = texture;
                return m;
            }

            /// <summary>Lit by the scene's lights (the shader the ground uses).</summary>
            static Material Lit(Color colour)
            {
                var shader = Shader.Find("Legacy Shaders/Diffuse") ?? Shader.Find("Diffuse") ?? Shader.Find("Sprites/Default");
                return new Material(shader) { color = colour };
            }

            /// <summary>A particle system of our own: soft round motes in a sphere, turning about it and rising, fading in and out.</summary>
            ParticleSystem Particles(string name, Vector3 at, Color colour, float size, float life, float rate, float radius, float orbit, float rise, float speed = 0.1f, Transform parent = null)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent != null ? parent : root.transform, false);
                go.transform.position = at;
                var ps = go.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.loop = true;
                main.startLifetime = life;
                main.startSpeed = speed;
                main.startSize = new ParticleSystem.MinMaxCurve(size * 0.7f, size * 1.3f);
                main.startColor = colour;
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 500;
                var emission = ps.emission;
                emission.rateOverTime = rate;
                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = Mathf.Max(0.001f, radius);
                var vel = ps.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.Local;
                vel.x = 0f; vel.y = rise; vel.z = 0f;
                vel.orbitalX = 0f; vel.orbitalY = orbit; vel.orbitalZ = 0f;
                var fade = ps.colorOverLifetime;
                fade.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0.8f, 0.7f), new GradientAlphaKey(0f, 1f) });
                fade.color = g;
                var grow = ps.sizeOverLifetime;
                grow.enabled = true;
                grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.3f));
                var r = go.GetComponent<ParticleSystemRenderer>();
                r.sharedMaterial = Unlit(Color.white, Soft());
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ps.Play();
                return ps;
            }

            /// <summary>A particle system stops making motes (after <paramref name="after"/> s) and goes once the last has faded.</summary>
            void StopParticles(ParticleSystem ps, float after = 0f) => Plugin.Instance.StartCoroutine(Stopping(ps, after));

            IEnumerator Stopping(ParticleSystem ps, float after)
            {
                for (float t0 = Now; ps != null && Now < t0 + after;) yield return null;
                if (ps == null) yield break;
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                Object.Destroy(ps.gameObject, ps.main.startLifetime.constantMax + 0.5f);
            }

            /// <summary>A soft white disc fading to nothing at its rim.</summary>
            static Texture2D Soft()
            {
                if (soft != null) return soft;
                const int n = 64;
                soft = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "u5_soft" };
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    soft.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
                soft.Apply(false);
                return soft;
            }

            /// <summary>The symbol of the Codex of Ultimate Wisdom as the story draws it: two interlaced triangles in a ring.</summary>
            static Texture2D CodexSign()
            {
                if (codexSign != null) return codexSign;
                const int n = 128;
                codexSign = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "u5_codex" };
                var corners = new List<Vector2>();
                foreach (float start in new[] { 90f, 270f })
                    for (int k = 0; k < 3; k++)
                    {
                        float a = (start + k * 120f) * Mathf.Deg2Rad;
                        corners.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.72f);
                    }
                float Segment(Vector2 p, Vector2 a, Vector2 b)
                {
                    var ab = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                    return (p - (a + ab * t)).magnitude;
                }
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var p = new Vector2((x + 0.5f) / n * 2f - 1f, (y + 0.5f) / n * 2f - 1f);
                    float d = Mathf.Abs(p.magnitude - 0.86f);
                    for (int t = 0; t < 2; t++)
                        for (int k = 0; k < 3; k++)
                            d = Mathf.Min(d, Segment(p, corners[t * 3 + k], corners[t * 3 + (k + 1) % 3]));
                    float a = Mathf.Clamp01(1f - (d - 0.025f) / 0.03f);
                    codexSign.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
                codexSign.Apply(true);
                return codexSign;
            }
        }
    }
}
