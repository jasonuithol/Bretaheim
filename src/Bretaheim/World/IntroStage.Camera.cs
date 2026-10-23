using System;
using System.Linq;
using Bretaheim.Data;
using UnityEngine;

namespace Bretaheim.WorldGen
{
    public static partial class IntroStage
    {
        /// <summary>
        /// The camera's shots: each scene opens on one of its own (the bed in the dark, the
        /// treetops, the storm from on high, the hut in its clearing) and the moments the
        /// Introduction dwells on are framed closer (the circle over the bed, the glass case, the
        /// stones, the gate, Shamino's greeting, the Shadowlords over the friends' shoulders, the
        /// bolt from the side, Shamino from above, the knock at Iolo's door, the table and the
        /// hearth for his tale); between them the camera follows the Avatar from above and south,
        /// as the original looks down on its strip. Its lights go with it: a soft light over what
        /// is watched and a faint fill from the camera.
        /// </summary>
        partial class Director
        {
            const float WideFov = 62f, CloseFov = 52f;
            /// <summary>The shot now (where the camera is, what it looks at), null: following the Avatar.</summary>
            Func<(Vector3 at, Vector3 look)> shot;
            float shotEase = 1.2f, shotFov = WideFov, fov = WideFov;
            bool cutting;
            Light fill;

            void Shoot(Func<(Vector3 at, Vector3 look)> s, float ease = 1f, float lens = WideFov, bool cut = false)
            {
                shot = s;
                shotEase = ease;
                shotFov = lens;
                cutting |= cut;
            }

            void Follow()
            {
                shot = null;
                shotFov = WideFov;
            }

            Actor Avatar => slots.FirstOrDefault(a => a?.Go != null && a.Who.StartsWith("avatar"));
            Actor Slot(int i) => slots[i]?.Go != null ? slots[i] : null;
            static Vector3 Of(Actor a, Vector3 otherwise) => a?.Go != null ? a.Pos : otherwise;

            /// <summary>A shot that keeps on a figure (or a place, once it is gone): from <paramref name="offset"/> of it, looking at it <paramref name="up"/> high.</summary>
            Func<(Vector3, Vector3)> On(Func<Actor> who, Vector3 otherwise, Vector3 offset, float up = 1f)
            {
                var last = otherwise;
                return () =>
                {
                    var a = who();
                    if (a?.Go != null) last = a.Pos;
                    return (last + offset, last + Vector3.up * up);
                };
            }

            /// <summary>A shot from a place, creeping slowly in on what it looks at (a held shot is never quite still).</summary>
            Func<(Vector3, Vector3)> Fixed(Vector3 at, Vector3 look)
            {
                float from = Now;
                var towards = (look - at).normalized;
                float most = Mathf.Min(1.6f, (look - at).magnitude * 0.3f);
                return () => (at + towards * Mathf.Min(most, (Now - from) * 0.09f), look);
            }

            /// <summary>Each scene's first shot, drifted into slowly from where the camera was put.</summary>
            void Opening(int n)
            {
                switch (n)
                {
                    case 0:
                    {
                        // the bed in the dark, the window's moonlight on it
                        var bed = BedAt();
                        Shoot(Fixed(bed + new Vector3(3.4f, 4.4f, -4.4f), bed + Vector3.up * 0.2f), 0.3f, CloseFov);
                        break;
                    }
                    case 1:
                        // from the path, up into the treetops and the clouded moon
                        Shoot(Fixed(Cell(3, 3) + new Vector3(0f, 1.4f, -2f), Cell(7, -5) + Vector3.up * 11f), 0.25f, WideFov);
                        break;
                    case 2:
                        // the storm over the clearing, from on high
                        Shoot(Fixed(Cell(9, 2) + new Vector3(0f, 15f, -13f), Cell(9, 1)), 0.35f, WideFov);
                        break;
                    case 3:
                        // the hut in its clearing, its light among the trees
                        Shoot(Fixed(Cell(9, 6) + new Vector3(0f, 9f, -3f), Cell(9, 2)), 0.4f, WideFov);
                        break;
                }
            }

            /// <summary>The shot for a step of the script (before it plays).</summary>
            void Frame(ViewEvent ev)
            {
                switch (scene)
                {
                    case 0:
                        switch (ev.At)
                        {
                            case 0x20a:
                                // from the foot of the bed, up at the circle
                                Shoot(Fixed(Cell(6, 3) + new Vector3(0.8f, 1.5f, -1.8f), Cell(4, 2) + Vector3.up * 1.9f), 0.7f, WideFov);
                                break;
                            case 0x213:
                            {
                                var bed = BedAt();
                                Shoot(Fixed(bed + new Vector3(1.8f, 2.6f, -2.4f), bed), 0.9f, CloseFov);
                                break;
                            }
                            case 0x21b:
                                Shoot(On(() => Avatar, Cell(3, 2), new Vector3(0f, 7.2f, -6.2f), 0.4f), 1.3f);
                                break;
                            case 0x253:
                                // the glass case on its shelf, the Avatar before it
                                Shoot(Fixed(Cell(15, 3) + new Vector3(0.2f, 2.3f, -0.4f), Cell(15, 1) + Vector3.up * 0.9f), 0.9f, WideFov);
                                break;
                            case 0x269:
                                Shoot(On(() => Avatar, Cell(15, 2), new Vector3(0f, 7.2f, -6.2f), 0.4f), 1.1f);
                                break;
                            case 0x29f:
                                // one hand on the door, a look back
                                Shoot(Fixed(Cell(8, 3) + new Vector3(0.5f, 2.4f, -1f), Cell(7, 0) + Vector3.up * 1.2f), 0.9f, CloseFov);
                                break;
                        }
                        break;
                    case 1:
                        switch (ev.At)
                        {
                            case 0x2b7:
                                // along the path beside the Avatar
                                Shoot(On(() => Avatar, Cell(0, 0), new Vector3(2.5f, 3.2f, -6.5f), 0.9f), 0.9f);
                                break;
                            case 0x2e0:
                                // before the stones: the amulet, the shimmering, the Ankh
                                Shoot(Fixed(Cell(13, 3) + new Vector3(0.3f, 3.4f, -0.6f), Cell(13, 1) + Vector3.up * 0.5f), 0.8f, CloseFov);
                                break;
                            case 0x2e2:
                                // the door springs up before the Avatar
                                Shoot(Fixed(Cell(13, 3) + new Vector3(0.6f, 2.4f, -1.8f), Cell(13, 1) + Vector3.up * 2f), 1f, WideFov);
                                break;
                        }
                        break;
                    case 2:
                        switch (ev.At)
                        {
                            case 0x2f6:
                                // the clearing as Shamino comes through it
                                Shoot(On(() => Slot(1), Cell(4, 0), new Vector3(1.5f, 4.5f, -8f), 0.9f), 0.8f);
                                break;
                            case 0x31a:
                                Shoot(Fixed(Cell(9, 3) + new Vector3(3.5f, 2.6f, -5.5f), Cell(9, 1) + Vector3.up * 1.6f), 0.9f);
                                break;
                            case 0x330:
                            {
                                // the two friends
                                Shoot(() =>
                                {
                                    var mid = (Of(Avatar, Cell(9, 2)) + Of(Slot(1), Cell(10, 2))) / 2f;
                                    return (mid + new Vector3(0.5f, 2.3f, -4.6f), mid + Vector3.up * 1.2f);
                                }, 1f, CloseFov);
                                break;
                            }
                            case 0x33e:
                                // over their shoulders, at the darkness taking shape
                                Shoot(Fixed(Cell(13, 3) + new Vector3(0.5f, 2.5f, -1.2f), Cell(6, 1) + Vector3.up * 1.2f), 0.8f, WideFov);
                                break;
                            case 0x36b:
                            {
                                // the bolt, from the side
                                var mid = (Cell(7, 1) + Cell(10, 2)) / 2f;
                                Shoot(Fixed(mid + new Vector3(0f, 1.9f, -6f), mid + Vector3.up * 1.3f), 1.6f, WideFov);
                                break;
                            }
                            case 0x377:
                                // Shamino at the Avatar's feet, from above
                                Shoot(Fixed(Cell(10, 2) + new Vector3(0.3f, 6.5f, -1.6f), Cell(10, 2)), 1f, CloseFov);
                                break;
                            case 0x384:
                                // the amulet's glow, the Shadowlords shrinking back before it
                                Shoot(() => { var a = Of(Avatar, Cell(9, 2)); return (Cell(5, 3) + new Vector3(0f, 1.6f, -2.5f), a + Vector3.up * 1.2f); }, 1.2f, WideFov);
                                break;
                            case 0x39f:
                                Shoot(On(() => Avatar, Cell(10, 3), new Vector3(1.6f, 2.4f, -3.8f), 1f), 1f, CloseFov);
                                break;
                            case 0x3b0:
                                Shoot(On(() => Avatar, Cell(11, 2), new Vector3(-2f, 5f, -8f), 0.8f), 0.8f);
                                break;
                        }
                        break;
                    case 3:
                        switch (ev.At)
                        {
                            case 0x3e1:
                                Follow();
                                break;
                            case 0x41e:
                                // outside the door, the friends come to knock
                                Shoot(Fixed(Cell(8, 8) + new Vector3(2f, 1.9f, -0.8f), Cell(8, 4) + Vector3.up * 1.3f), 0.9f, CloseFov);
                                break;
                            case 0x42f:
                                // the door open: Iolo and the friends within
                                Shoot(Fixed(Cell(8, 4) + new Vector3(0f, 4.4f, -3.2f), Cell(7, 2)), 0.8f, WideFov);
                                break;
                            case 0x47d:
                                Shoot(Fixed(Cell(10, 3) + new Vector3(0.6f, 2.5f, -2.4f), Cell(10, 2) + Vector3.up * 0.9f), 0.7f, CloseFov);
                                break;
                        }
                        break;
                }
            }

            /// <summary>The shots of Iolo's tale: the hearth, the table, the friend abed, by turns.</summary>
            void TaleShot(int p)
            {
                var hearth = hearthAt ?? Cell(10, 1);
                var table = Cell(10, 2) + Vector3.up * 0.9f;
                switch (p % 3)
                {
                    case 0:
                        Shoot(Fixed(hearth + new Vector3(1.4f, 1.6f, -3.6f), hearth + Vector3.up * 1.1f), 0.35f, CloseFov);
                        break;
                    case 1:
                        Shoot(Fixed(table + new Vector3(-2.2f, 1.6f, -2.6f), table), 0.35f, CloseFov);
                        break;
                    default:
                        Shoot(Fixed(Cell(8, 3) + new Vector3(0f, 3.4f, -2.2f), (table + hearth) / 2f), 0.35f, WideFov);
                        break;
                }
            }

            /// <summary>Where the camera wants to be this frame, eased towards; the lights put with it.</summary>
            void MoveCamera(float dt)
            {
                var focus = Focus();
                Vector3 wantAt, wantLook;
                float ease;
                if (shot != null)
                {
                    (wantAt, wantLook) = shot();
                    ease = shotEase;
                }
                else
                {
                    var offset = scene == 0 ? new Vector3(0f, 8.5f, -9f) : tale ? new Vector3(0f, 5.5f, -7f) : new Vector3(0f, 8f, -11f);
                    wantAt = focus + offset;
                    wantLook = focus + Vector3.down * 1.2f;
                    ease = tale ? 0.3f : 1.2f;
                }
                if (cutting)
                {
                    camAt = wantAt;
                    camLook = wantLook;
                    fov = shotFov;
                    cutting = false;
                }
                float k = 1f - Mathf.Exp(-dt * ease);
                camLook = Vector3.Lerp(camLook, wantLook, k);
                camAt = Vector3.Lerp(camAt, wantAt, k);
                fov = Mathf.Lerp(fov, shotFov, k);
                Stage.CameraAt = camAt;
                Stage.LookAt = camLook;
                Stage.Fov = fov;
                // a soft light from above on what is watched, a faint fill from the camera's side
                var subject = shot != null ? camLook : focus;
                if (lamp != null) lamp.transform.position = subject + new Vector3(0f, 7f, -3f);
                if (fill == null && root != null)
                {
                    fill = Stage.Glow(camAt, new Color(0.7f, 0.75f, 0.9f), 0.35f, 9f, root.transform).GetComponent<Light>();
                    fill.name = "u5_intro_fill";
                }
                if (fill != null) fill.transform.position = Vector3.Lerp(camAt, camLook, 0.35f) + Vector3.up * 0.5f;
            }
        }
    }
}
