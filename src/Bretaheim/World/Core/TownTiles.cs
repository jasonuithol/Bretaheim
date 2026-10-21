using System;
using System.Collections.Generic;
using System.Globalization;
using Bretaheim.Data;

namespace Bretaheim.WorldGen.Core
{
    public enum TileKind
    {
        Ground, Wall, Window, Door, Arch, Roof, Battlement, Tree, FruitTree, Crop, Water, FenceEW, FenceNS,
        Stairs, LadderUp, LadderDown, BedHead, BedFoot, Table, Chair, Prop, Sign,
    }

    public enum Paint { None, Dirt, Cultivated, Paved, Planks }

    /// <summary>How a wall tile is built (Constructing world assets.txt).</summary>
    public enum WallBuild { Brick, Cobble, Shelf }

    /// <summary>Where a part's (x, y, z) puts it: the middle of its bounds, standing on y, its top at y, or its pivot.</summary>
    public enum PartPlace { Centre, Stand, Top, Pivot }

    /// <summary>One piece of a composite prop, in tile-local metres (x east, z north, y up from the floor).</summary>
    public struct Part
    {
        public string Prefab;
        public float X, Y, Z, Yaw;
        public float SX, SY, SZ;
        public PartPlace Place;
        /// <summary>A changed look applied in game ("white_marble", "fountain"), or null.</summary>
        public string Look;
        /// <summary>The item an item stand holds, or null.</summary>
        public string Item;
        /// <summary>Degrees turned about its own east–west axis after its yaw (an item stand laid on its side lays its item flat).</summary>
        public float Tilt;
        /// <summary>Degrees turned about its own up axis before the tilt, and about its own north–south axis before that.</summary>
        public float Spin, Roll;
    }

    public class TileRule
    {
        public TileKind Kind = TileKind.Ground;
        public Paint Paint = Paint.None;
        public string Prefab;
        /// <summary>Several pieces making one object (wells, posts, stoves …), or null.</summary>
        public Part[] Parts;
        /// <summary>Prefabs one of which is picked per tile (rugs …), or null.</summary>
        public string[] Choices;
        /// <summary>Wall-mounted props: the side of the tile their wall is on (n/e/s/w yaw), or null.</summary>
        public float? WallSide;
        /// <summary>A wall hiding a secret door (revealed later, MASTER_PLAN §5.2).</summary>
        public bool Secret;
        /// <summary>A secret wall opened (and shut again) by playing the harpsichord's melody, not by a blow: no door behind it.</summary>
        public bool Tune;
        /// <summary>A trapdoor (0x8c): a stone floor with no collision, dropping whoever steps on it to the storey below.</summary>
        public bool Trapdoor;
        /// <summary>A floor grate (0x86): the top of a ladder down, an iron grate over the hole with no stone under it.</summary>
        public bool Grate;
        /// <summary>An arch over a gate (0x87): a portcullis just south of it, down at night (the drawbridges' hours).</summary>
        public bool Portcullis;
        /// <summary>Yaw in degrees the piece faces (n 0, e 90, s 180, w 270).</summary>
        public float Yaw;
        public WallBuild Build;
        /// <summary>Windows: an arrow slit. Doors: an iron gate / has a viewport / lock ("key", "magic").</summary>
        public bool Narrow, Gate, Viewport;
        public string Lock;
        /// <summary>Tables: candles (Prefab) stood on this tile of the table, or 0.</summary>
        public int Candles;
        /// <summary>Battlements: with merlons (the "no vertical projections" parapets have none).</summary>
        public bool Merlons = true;
        /// <summary>Part of a shop signpost: "post" (its foot, 0xe0) or "top" (0xe1/0xe2, beside the guild icon), else null.</summary>
        public string SignPost;
        /// <summary>River water: a trough (Prefab standing in it) inside a building, shallow lily water outside.</summary>
        public bool River;
        /// <summary>Bridge planks (0x48/0x49): a drawbridge where a line of them has water at both ends.</summary>
        public bool Drawbridge;
        /// <summary>A grave (0x89/0x8a): its eulogy (SIGNS.DAT) is read off the marker itself, and the earth before it (south) can be dug.</summary>
        public bool Grave;

        public bool IsBarrier => Kind == TileKind.Wall || Kind == TileKind.Window || Kind == TileKind.Door || Kind == TileKind.Arch || Kind == TileKind.Battlement;
        public bool IsOpening => Kind == TileKind.Window || Kind == TileKind.Door || Kind == TileKind.Arch;
        /// <summary>Open-air ground (grass, dirt, fields, plants, water): never floored or roofed, even inside walls.</summary>
        public bool IsOutdoor => Kind == TileKind.Tree || Kind == TileKind.FruitTree || Kind == TileKind.Crop || Kind == TileKind.Water
                                 || (Kind == TileKind.Ground && (Paint == Paint.None || Paint == Paint.Dirt || Paint == Paint.Cultivated));
    }

    /// <summary>Wall material: the pieces solid 2 m × 2 m × 3 m wall tiles are laid from.</summary>
    public class TownStyle
    {
        public string Brick, BrickSmall, Sill, Arch, Floor, Door, Gate, Merlon;
        /// <summary>The big block solid wall is laid with, two courses (2 m) high and 1 m deep, SlabLength metres long; or null.</summary>
        public string Slab;
        public int SlabLength = 4;
        /// <summary>A leaf of a double door: no frame of its own, centred on its tile, hinged on its local +x side.</summary>
        public string DoubleDoor;
    }

    /// <summary>A roof material: hip-roof pieces and how much a 2 m cell rises.</summary>
    public class RoofSet
    {
        public string Slope, OCorner, ICorner, Top, Corner, Corner2;
        public float Rise = 1;
        /// <summary>Grausten: 45° slopes with the pivot at mid-height; corners built from triangles.</summary>
        public bool Triangles => Corner != null;
    }

    /// <summary>The cobblestone cottage wall: a wooden frame filled with stone fence.</summary>
    public class CobbleStyle
    {
        public string Fence, Post, Post4, Beam, Beam4;
    }

    /// <summary>Armourers' display walls: item stands of the families of what each one sells.</summary>
    public class ArmouryDisplay
    {
        public string Stand;
        public float[] Rows = new float[0];
        /// <summary>How many tiles the display runs along the wall either side of the one behind the armourer.</summary>
        public int Reach = 3;
        /// <summary>Family name → the U5 item ids in it and the Valheim item prefabs shown for it.</summary>
        public readonly List<(string name, int[] u5, string[] models)> Families = new List<(string, int[], string[])>();
    }

    /// <summary>Small water features planted as flower beds.</summary>
    public class Garden
    {
        /// <summary>Location numbers whose small water features are planted.</summary>
        public int[] Locations = new int[0];
        public int MaxTiles;
        public int BushesPerTile, FlowersPerTile;
        public string[] Bushes = new string[0], Flowers = new string[0];
    }

    /// <summary>assets/mappings/town_tiles.json: tile rules, materials, materials per settlement kind.</summary>
    public class TownTiles
    {
        readonly TileRule[] rules = new TileRule[256];
        public readonly Dictionary<string, TownStyle> Styles = new Dictionary<string, TownStyle>();
        public readonly Dictionary<string, RoofSet> Roofs = new Dictionary<string, RoofSet>();
        public CobbleStyle Cobble = new CobbleStyle();
        /// <summary>Roof of a cobblestone cottage.</summary>
        public string CottageRoof = "thatch";
        public readonly Dictionary<string, (string walls, string roof)> Settlements = new Dictionary<string, (string, string)>();
        public ArmouryDisplay Armoury = new ArmouryDisplay();
        public Garden Garden = new Garden();
        /// <summary>Ground height of settlements with water, by location number (others: 1.5 m).</summary>
        public readonly Dictionary<int, float> WaterGround = new Dictionary<int, float>();
        /// <summary>Side (n/e/s/w) drawbridges hinge on, by location number (others: the side facing the middle of the map).</summary>
        public readonly Dictionary<int, string> DrawbridgeHinge = new Dictionary<int, string>();
        /// <summary>Where a settlement without guards sends for help, by location number (the Britannys: Lord British's castle).</summary>
        public readonly Dictionary<int, int> HelpFrom = new Dictionary<int, int>();
        /// <summary>A chair made grander at one place: (location, z, cell) → prefab and seat height over its base.</summary>
        public readonly Dictionary<(int, int, int), (string prefab, float seat)> ChairAt = new Dictionary<(int, int, int), (string, float)>();
        /// <summary>Where a dead player comes back (location, z, x, y), or none.</summary>
        public (int location, int z, int x, int y)? Respawn;
        /// <summary>Townsfolk moved from a place of their schedule: (location, slot, place) → (x, y, z).</summary>
        public readonly Dictionary<(int, int, int), (int x, int y, int z)> NpcPlaces = new Dictionary<(int, int, int), (int, int, int)>();
        /// <summary>Tiles a settlement's maps are read as instead, by location number (tile → tile).</summary>
        public readonly Dictionary<int, Dictionary<byte, byte>> TileAs = new Dictionary<int, Dictionary<byte, byte>>();
        /// <summary>One tile of a settlement's map read as another: (location, z, x, y) → tile.</summary>
        public readonly Dictionary<(int, int, int, int), byte> TileAt = new Dictionary<(int, int, int, int), byte>();

        public TileRule this[int tile] => rules[tile & 0xff];

        public static TownTiles Parse(string json)
        {
            var root = Json.Obj(Json.Parse(json));
            var t = new TownTiles();
            for (int i = 0; i < 256; i++) t.rules[i] = new TileRule();
            foreach (var kv in Json.Obj(root["tiles"]))
            {
                int id = int.Parse(kv.Key, NumberStyles.HexNumber);
                var o = Json.Obj(kv.Value);
                var r = new TileRule
                {
                    Kind = ParseKind(Json.Str(o, "kind")),
                    Prefab = Json.Str(o, "prefab"),
                    Narrow = Json.Bool(o, "narrow"),
                    Gate = Json.Bool(o, "gate"),
                    Viewport = Json.Bool(o, "viewport"),
                    Lock = Json.Str(o, "lock"),
                    Merlons = !o.ContainsKey("merlons") || Json.Bool(o, "merlons"),
                    Secret = Json.Bool(o, "secret"),
                    Tune = Json.Bool(o, "tune"),
                    Trapdoor = Json.Bool(o, "trapdoor"),
                    Grate = Json.Bool(o, "grate"),
                    Portcullis = Json.Bool(o, "portcullis"),
                    Candles = Json.Get(o, "candles") is double candles ? (int)candles : 0,
                    SignPost = Json.Str(o, "signpost"),
                    River = Json.Bool(o, "river"),
                    Drawbridge = Json.Bool(o, "drawbridge"),
                    Grave = Json.Bool(o, "grave"),
                };
                if (Json.Str(o, "wall") is string side) r.WallSide = FacingYaw(side);
                if (Json.Get(o, "choices") is List<object> choices) r.Choices = choices.ConvertAll(c => (string)c).ToArray();
                string build = Json.Str(o, "build");
                if (build != null) r.Build = (WallBuild)Enum.Parse(typeof(WallBuild), build, true);
                string paint = Json.Str(o, "paint");
                r.Paint = paint == null || paint == "grass" ? Paint.None : (Paint)Enum.Parse(typeof(Paint), paint, true);
                r.Yaw = FacingYaw(Json.Str(o, "facing"));
                if (Json.Get(o, "parts") is List<object> parts)
                {
                    r.Parts = new Part[parts.Count];
                    for (int i = 0; i < parts.Count; i++) r.Parts[i] = ParsePart(Json.Obj(parts[i]));
                }
                t.rules[id] = r;
            }
            foreach (var kv in Json.Obj(root["styles"]))
            {
                if (kv.Key.StartsWith("_")) continue;
                var o = Json.Obj(kv.Value);
                t.Styles[kv.Key] = new TownStyle
                {
                    Brick = Json.Str(o, "brick"), BrickSmall = Json.Str(o, "brick_small"), Sill = Json.Str(o, "sill"), Arch = Json.Str(o, "arch"),
                    Floor = Json.Str(o, "floor"), Door = Json.Str(o, "door"), DoubleDoor = Json.Str(o, "double_door"), Gate = Json.Str(o, "gate"), Merlon = Json.Str(o, "merlon"),
                    Slab = Json.Str(o, "slab"), SlabLength = Json.Get(o, "slab_length") is double sl ? (int)sl : 4,
                };
            }
            foreach (var kv in Json.Obj(root["roofs"]))
            {
                if (kv.Key.StartsWith("_")) continue;
                var o = Json.Obj(kv.Value);
                t.Roofs[kv.Key] = new RoofSet
                {
                    Slope = Json.Str(o, "slope"), OCorner = Json.Str(o, "ocorner"), ICorner = Json.Str(o, "icorner"), Top = Json.Str(o, "top"),
                    Corner = Json.Str(o, "corner"), Corner2 = Json.Str(o, "corner2"),
                    Rise = Json.Get(o, "rise") is double rise ? (float)rise : 1f,
                };
            }
            var cobble = Json.Obj(root["cobble"]);
            t.Cobble = new CobbleStyle
            {
                Fence = Json.Str(cobble, "fence"), Post = Json.Str(cobble, "post"), Post4 = Json.Str(cobble, "post_4"), Beam = Json.Str(cobble, "beam"), Beam4 = Json.Str(cobble, "beam_4"),
            };
            t.CottageRoof = Json.Str(cobble, "roof") ?? t.CottageRoof;
            foreach (var kv in Json.Obj(root["settlements"]))
            {
                if (kv.Key.StartsWith("_")) continue;
                var o = Json.Obj(kv.Value);
                t.Settlements[kv.Key] = (Json.Str(o, "walls"), Json.Str(o, "roof"));
            }
            if (root.TryGetValue("water_ground", out var wg))
                foreach (var kv in Json.Obj(wg))
                    if (!kv.Key.StartsWith("_")) t.WaterGround[int.Parse(kv.Key)] = (float)(double)kv.Value;
            if (root.TryGetValue("drawbridge_hinge", out var dh))
                foreach (var kv in Json.Obj(dh))
                    if (!kv.Key.StartsWith("_")) t.DrawbridgeHinge[int.Parse(kv.Key)] = (string)kv.Value;
            if (root.TryGetValue("tile_as", out var ta))
                foreach (var kv in Json.Obj(ta))
                {
                    if (kv.Key.StartsWith("_")) continue;
                    var map = t.TileAs[int.Parse(kv.Key)] = new Dictionary<byte, byte>();
                    foreach (var m in Json.Obj(kv.Value)) map[byte.Parse(m.Key, NumberStyles.HexNumber)] = byte.Parse((string)m.Value, NumberStyles.HexNumber);
                }
            if (root.TryGetValue("tile_at", out var tat))
                foreach (var kv in Json.Obj(tat))
                {
                    if (kv.Key.StartsWith("_")) continue;
                    foreach (var m in Json.Obj(kv.Value))
                    {
                        // "z:x,y": "tile"
                        var zxy = m.Key.Split(':', ',');
                        t.TileAt[(int.Parse(kv.Key), int.Parse(zxy[0]), int.Parse(zxy[1]), int.Parse(zxy[2]))] = byte.Parse((string)m.Value, NumberStyles.HexNumber);
                    }
                }
            if (root.TryGetValue("npc_places", out var np))
                foreach (var loc in Json.Obj(np))
                {
                    if (loc.Key.StartsWith("_")) continue;
                    foreach (var slot in Json.Obj(loc.Value))
                        foreach (var place in Json.Obj(slot.Value))
                        {
                            var xyz = (List<object>)place.Value;
                            t.NpcPlaces[(int.Parse(loc.Key), int.Parse(slot.Key), int.Parse(place.Key))] = ((int)(double)xyz[0], (int)(double)xyz[1], (int)(double)xyz[2]);
                        }
                }
            if (root.TryGetValue("chair_at", out var ca))
                foreach (var loc in Json.Obj(ca))
                {
                    if (loc.Key.StartsWith("_")) continue;
                    foreach (var at in Json.Obj(loc.Value))
                    {
                        var zxy = at.Key.Split(':', ',');
                        var o = Json.Obj(at.Value);
                        t.ChairAt[(int.Parse(loc.Key), int.Parse(zxy[0]), int.Parse(zxy[2]) * 32 + int.Parse(zxy[1]))] = (Json.Str(o, "prefab"), Json.Get(o, "seat") is double seat ? (float)seat : 0.1f);
                    }
                }
            if (root.TryGetValue("respawn", out var rs))
            {
                var o = Json.Obj(rs);
                int I(string k) => (int)(double)Json.Get(o, k);
                t.Respawn = (I("location"), I("z"), I("x"), I("y"));
            }
            if (root.TryGetValue("help_from", out var hf))
                foreach (var kv in Json.Obj(hf))
                    if (!kv.Key.StartsWith("_")) t.HelpFrom[int.Parse(kv.Key)] = (int)(double)kv.Value;
            if (root.TryGetValue("garden", out var gdn))
            {
                var o = Json.Obj(gdn);
                int I(string k) => Json.Get(o, k) is double v ? (int)v : 0;
                string[] L(string k) => Json.Get(o, k) is List<object> l ? l.ConvertAll(x => (string)x).ToArray() : new string[0];
                t.Garden = new Garden { Locations = Json.Get(o, "locations") is List<object> locs ? locs.ConvertAll(x => (int)(double)x).ToArray() : new int[0], MaxTiles = I("max_tiles"), BushesPerTile = I("bushes_per_tile"), FlowersPerTile = I("flowers_per_tile"), Bushes = L("bushes"), Flowers = L("flowers") };
            }
            if (root.TryGetValue("armoury", out var arm))
            {
                var o = Json.Obj(arm);
                t.Armoury.Stand = Json.Str(o, "stand");
                if (Json.Get(o, "rows") is List<object> rows) t.Armoury.Rows = rows.ConvertAll(x => (float)(double)x).ToArray();
                if (Json.Get(o, "reach") is double reach) t.Armoury.Reach = (int)reach;
                foreach (var kv in Json.Obj(o["families"]))
                {
                    var f = Json.Obj(kv.Value);
                    t.Armoury.Families.Add((kv.Key, Json.Arr(f["u5"]).ConvertAll(x => (int)(double)x).ToArray(),
                                            Json.Arr(f["models"]).ConvertAll(x => (string)x).ToArray()));
                }
            }
            return t;
        }

        static Part ParsePart(Dictionary<string, object> o)
        {
            float F(string k, float d = 0) => Json.Get(o, k) is double v ? (float)v : d;
            var p = new Part
            {
                Prefab = Json.Str(o, "prefab"), X = F("x"), Y = F("y"), Z = F("z"), Yaw = F("yaw"),
                SX = 1, SY = 1, SZ = 1,
                Place = Json.Str(o, "place") == "stand" ? PartPlace.Stand : Json.Str(o, "place") == "top" ? PartPlace.Top
                        : Json.Str(o, "place") == "pivot" ? PartPlace.Pivot : PartPlace.Centre,
                Look = Json.Str(o, "look"),
                Item = Json.Str(o, "item"),
                Tilt = F("tilt"),
                Spin = F("spin"),
                Roll = F("roll"),
            };
            if (Json.Get(o, "scale") is List<object> s)
            {
                p.SX = (float)(double)s[0];
                p.SY = (float)(double)s[1];
                p.SZ = (float)(double)s[2];
            }
            return p;
        }

        static TileKind ParseKind(string s)
        {
            switch (s)
            {
                case "fence_ew": return TileKind.FenceEW;
                case "fence_ns": return TileKind.FenceNS;
                case "ladder_up": return TileKind.LadderUp;
                case "ladder_down": return TileKind.LadderDown;
                case "bed_head": return TileKind.BedHead;
                case "bed_foot": return TileKind.BedFoot;
                case "fruittree": return TileKind.FruitTree;
                default: return (TileKind)Enum.Parse(typeof(TileKind), s, true);
            }
        }

        public static float FacingYaw(string facing)
        {
            switch (facing)
            {
                case "e": return 90;
                case "s": return 180;
                case "w": return 270;
                default: return 0;
            }
        }
    }
}
