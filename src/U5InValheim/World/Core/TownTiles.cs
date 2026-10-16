using System;
using System.Collections.Generic;
using System.Globalization;
using U5InValheim.Data;

namespace U5InValheim.WorldGen.Core
{
    public enum TileKind
    {
        Ground, Wall, Window, Door, Roof, Battlement, Tree, FruitTree, Crop, Water, FenceEW, FenceNS,
        Stairs, LadderUp, LadderDown, BedHead, BedFoot, Table, Chair, Prop,
    }

    public enum Paint { None, Dirt, Cultivated, Paved, Planks }

    public class TileRule
    {
        public TileKind Kind = TileKind.Ground;
        public Paint Paint = Paint.None;
        public string Prefab;
        /// <summary>Yaw in degrees the piece faces (n 0, e 90, s 180, w 270).</summary>
        public float Yaw;
        public bool Narrow, Open, Gate;

        public bool IsBarrier => Kind == TileKind.Wall || Kind == TileKind.Window || Kind == TileKind.Door || Kind == TileKind.Battlement;
        public bool IsOpening => Kind == TileKind.Window || Kind == TileKind.Door;
        /// <summary>Open-air ground (grass, dirt, fields, plants, water): never floored or roofed, even inside walls.</summary>
        public bool IsOutdoor => Kind == TileKind.Tree || Kind == TileKind.FruitTree || Kind == TileKind.Crop || Kind == TileKind.Water
                                 || (Kind == TileKind.Ground && (Paint == Paint.None || Paint == Paint.Dirt || Paint == Paint.Cultivated));
    }

    public class TownStyle
    {
        public string Wall, WallStub, WallHalf, Floor, FloorSmall, Roof, RoofTop, Gable, Door, Pole, Merlon;
        public bool Gabled;
    }

    /// <summary>assets/mappings/town_tiles.json: tile rules, building styles, style per settlement kind.</summary>
    public class TownTiles
    {
        readonly TileRule[] rules = new TileRule[256];
        public readonly Dictionary<string, TownStyle> Styles = new Dictionary<string, TownStyle>();
        public readonly Dictionary<string, (string houses, string perimeter)> Settlements = new Dictionary<string, (string, string)>();

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
                    Open = Json.Bool(o, "open"),
                    Gate = Json.Bool(o, "gate"),
                };
                string paint = Json.Str(o, "paint");
                r.Paint = paint == null || paint == "grass" ? Paint.None : (Paint)Enum.Parse(typeof(Paint), paint, true);
                r.Yaw = FacingYaw(Json.Str(o, "facing"));
                t.rules[id] = r;
            }
            foreach (var kv in Json.Obj(root["styles"]))
            {
                var o = Json.Obj(kv.Value);
                t.Styles[kv.Key] = new TownStyle
                {
                    Wall = Json.Str(o, "wall"), WallStub = Json.Str(o, "wall_stub"), WallHalf = Json.Str(o, "wall_half"),
                    Floor = Json.Str(o, "floor"), FloorSmall = Json.Str(o, "floor_small"), Roof = Json.Str(o, "roof"), RoofTop = Json.Str(o, "roof_top"),
                    Gable = Json.Str(o, "gable"), Door = Json.Str(o, "door"), Pole = Json.Str(o, "pole"),
                    Merlon = Json.Str(o, "merlon"), Gabled = Json.Str(o, "roof_kind") == "gable",
                };
            }
            foreach (var kv in Json.Obj(root["settlements"]))
            {
                if (kv.Key.StartsWith("_")) continue;
                var o = Json.Obj(kv.Value);
                t.Settlements[kv.Key] = (Json.Str(o, "houses"), Json.Str(o, "perimeter"));
            }
            return t;
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
