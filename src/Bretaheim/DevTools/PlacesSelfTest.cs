using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Bretaheim.WorldGen;
using Bretaheim.WorldGen.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Bretaheim.DevTools
{
    /// <summary>
    /// Part of the town self-test (U5_SELFTEST=town:&lt;loc&gt;): the settlement's stairs (is each
    /// flight's ramp where the steps are, is its stairwell open, is there floor at its landings)
    /// and its furnishings (how many, what their hover says).
    /// </summary>
    public static class PlacesSelfTest
    {
        static void Log(string s) => Plugin.Log.LogInfo("townsfolk self-test town: " + s);

        public static void Check(Settlements.Built town, List<ZNetView> near)
        {
            var o = town.Origin;
            foreach (var st in near.Select(v => v.GetComponent<U5Stairs>()).Where(s => s != null))
            {
                var t = st.transform;
                float scale = t.lossyScale.y;
                int onRamp = 0, points = 0;
                float worst = 0;
                var others = new HashSet<string>();
                for (int k = 1; k < 8; k++)
                {
                    float yaw = st.Turn * k / 8f, want = SettlementPlanner.StairRise * k / 8f;
                    var local = Quaternion.Euler(0, yaw, 0) * new Vector3(0, 0, 0.6f);
                    var from = t.TransformPoint(local + Vector3.up * (want + 0.9f / scale));
                    points++;
                    // (the first solid thing under the point, ignoring townsfolk)
                    var hits = Physics.RaycastAll(from, Vector3.down, 2f).Where(h => !h.collider.isTrigger && h.collider.GetComponentInParent<Character>() == null).OrderBy(h => h.distance).ToList();
                    if (hits.Count > 0 && hits[0].collider.GetComponent<StairRamp>() != null)
                    {
                        onRamp++;
                        worst = Mathf.Max(worst, Mathf.Abs(t.InverseTransformPoint(hits[0].point).y - want));
                    }
                    else others.Add(hits.Count == 0 ? "nothing" : $"{hits[0].collider.transform.root.name}/{hits[0].collider.name} at {t.InverseTransformPoint(hits[0].point).y:0.0}");
                }
                // the stairwell: nothing solid over the last half turn (but the stair's own ramp)
                var top = t.TransformPoint(Quaternion.Euler(0, st.Turn - 90, 0) * new Vector3(0, SettlementPlanner.StairRise - 0.8f, 0.6f));
                var blocked = Physics.RaycastAll(top, Vector3.up, 2.2f).Where(h => h.collider.GetComponentInParent<U5Stairs>() == null).Select(h => h.collider.transform.root.name).Distinct().ToList();
                // the landings: floor (or ground) a step beyond the top and the foot
                float exitYaw = st.Turn;
                var exit = t.TransformPoint(Quaternion.Euler(0, exitYaw, 0) * new Vector3(0, SettlementPlanner.StairRise + 0.5f / scale, 1.6f));
                var entry = t.TransformPoint(new Vector3(0, 0.5f / scale, 1.6f));
                string Floor(Vector3 at) => Physics.Raycast(at, Vector3.down, out var h, 1.5f) ? $"{h.collider.transform.root.name} {at.y - 0.5f - h.point.y:0.00} m down" : "nothing";
                var l = t.position - o;
                int cell = town.Plan.CellAt(l.x, l.z);
                Log($"stairs {Utils.GetPrefabName(st.gameObject)} at tile ({cell % 32},{cell / 32}) y {l.y:0.0} yaw {t.eulerAngles.y:0} scale {scale:0.00}: ramp under {onRamp}/{points} steps (worst {worst:0.00} m off{(others.Count > 0 ? "; else " + string.Join(", ", others) : "")}), stairwell {(blocked.Count == 0 ? "open" : "blocked by " + string.Join(", ", blocked))}, top landing on {Floor(exit)}, foot on {Floor(entry)}, hover \"{st.GetHoverText().Replace("\n", " ")}\"");
            }
            void Count<T>(string what) where T : Component
            {
                var all = near.SelectMany(v => v.GetComponentsInChildren<T>()).ToList();
                if (all.Count == 0) return;
                string hover = all[0] is Hoverable h ? h.GetHoverText().Replace("\n", " | ") : "";
                Log($"{all.Count} {what}, e.g. at {all[0].transform.position - o}: \"{hover}\"");
            }
            Count<Furnishings.TelescopeView>("telescope parts");
            Count<Furnishings.WishingWell>("well parts");
            Count<Furnishings.GrandfatherClock>("clock faces");
            Count<Furnishings.GraveMarker>("grave markers");
            Count<Furnishings.GravePlot>("graves' earth");
            Count<MirrorGlass>("mirrors");
            Count<Furnishings.CrystalSphere>("crystal spheres");
            var fences = Object.FindObjectsByType<FenceKlimb>(FindObjectsSortMode.None).Where(f => Vector3.Distance(f.transform.position, o) < town.Site.Half + 5f).ToList();
            if (fences.Count > 0) Log($"{fences.Count} klimbable fences, e.g. \"{fences[0].GetHoverText().Replace("\n", " | ")}\"");
        }

        /// <summary>
        /// U5_SELFTEST=places: three bridges built with their zones (pieces spawned, the deck under
        /// foot at its height, the river under it) and the ground at each waterfall (the cliff: the
        /// land beside the river upstream against downstream, and the river through it).
        /// </summary>
        public static IEnumerator Overworld(Func<Vector3, IEnumerator> generate)
        {
            void Say(string s) => Plugin.Log.LogInfo("places self-test " + s);
            var shape = Britannia.Active;
            var all = Bridges.All();
            Say($"{all.Count} bridges, {all.Sum(b => b.pieces.Count)} pieces in all");
            foreach (var (centre, pieces) in all.Take(3))
            {
                yield return generate(centre);
                yield return new WaitForSeconds(2f);
                var deck = pieces.First(p => p.Prefab == "wood_floor" && !p.Turned);
                float want = Britannia.SeaLevel + deck.Y + 0.097f;
                var near = Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None).Where(v => Vector3.Distance(v.transform.position, centre) < 50f && v.GetZDO() != null && v.GetZDO().GetBool(Settlements.FixedKey)).ToList();
                var top = Physics.RaycastAll(new Vector3(centre.x, want + 3f, centre.z), Vector3.down, 10f).Where(h => !h.collider.isTrigger).OrderBy(h => h.distance).ToList();
                float ground = ZoneSystem.instance.GetGroundHeight(centre) - Britannia.SeaLevel;
                Say($"bridge at {centre}: {near.Count} of its {pieces.Count} pieces found ({string.Join(", ", near.GroupBy(v => Utils.GetPrefabName(v.gameObject)).Select(g => $"{g.Key} {g.Count()}"))}); under foot in the middle {(top.Count > 0 ? $"{Utils.GetPrefabName(top[0].collider.transform.root.gameObject)} at {top[0].point.y - Britannia.SeaLevel:0.00} m (deck {want - Britannia.SeaLevel:0.00})" : "nothing")}; river bed {ground:0.0} m; water {(ZoneSystem.instance.GetSolidHeight(centre) < Britannia.SeaLevel ? "yes" : "no")}");
            }
            var cliffs = new Cliffs(shape.Map, shape.Terrain);
            foreach (var (fx, fy, dx, dy) in cliffs.Falls)
            {
                shape.TileCentre(fx - 0.5, fy - 0.5, out float cx, out float cz);
                var at = new Vector3(cx, Britannia.SeaLevel, cz);
                yield return generate(at);
                yield return new WaitForSeconds(1f);
                string Side(int side, float along)
                {
                    // (u east = +x, v south = −z)
                    float wx = cx + (float)((side * dy * 1.4 - dx * along) * shape.TileMetres);
                    float wz = cz - (float)((-side * dx * 1.4 - dy * along) * shape.TileMetres);
                    return $"{ZoneSystem.instance.GetGroundHeight(new Vector3(wx, 0, wz)) - Britannia.SeaLevel:0.0}";
                }
                Say($"waterfall ({fx - 0.5},{fy - 0.5}) falling ({dx},{dy}): river {ZoneSystem.instance.GetGroundHeight(at) - Britannia.SeaLevel:0.0} m; land beside it a quarter tile upstream / downstream: west-or-north side {Side(-1, 0.25f)} / {Side(-1, -0.25f)}, other side {Side(1, 0.25f)} / {Side(1, -0.25f)}");
            }
        }
    }
}
