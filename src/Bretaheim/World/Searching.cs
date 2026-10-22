using System.Linq;
using UnityEngine;

namespace Bretaheim.WorldGen
{
    /// <summary>
    /// Searching the settlements' furniture as the original's Search does any tile (SJOG
    /// cmd_search): "Under the bed thou dost find …", "In the barrel thou dost find …", then what is
    /// hidden there (a hidden spot Treasure placed on that tile) or "nothing of note.". A bed that is
    /// not the player's (every town bed but the inn bed they have paid for) offers Search; a barrel
    /// (Valheim's fermenter) always does, and nothing else (it brews nothing in Britannia).
    /// </summary>
    public static class Searching
    {
        public const int SUnderTheBed = 0x89ca, SInTheBarrel = 0x89a6, SInTheStump = 0x8950;
        const int SThouDostFind = 0x8a38;
        /// <summary>How far a hidden spot may lie from a bed's pivot (two tiles of 2 m, the pivot between them), and from a barrel's.</summary>
        public const float BedReach = 2.3f, TileReach = 1.2f;

        public static bool IsBed(string prefab) => prefab == "bed" || prefab == "piece_bed02";

        /// <summary>"Under the bed thou dost find " (the original's words, on one line).</summary>
        static string Prefix(int where)
        {
            var ovl = Plugin.U5.Ovl;
            return (ovl.CStr(where) + ovl.CStr(SThouDostFind)).Replace("\n", " ").Trim() + " ";
        }

        /// <summary>Search the furniture at <paramref name="at"/>: the hidden thing nearest it within reach, taken as a search spot would be.</summary>
        public static void Search(Vector3 at, float reach, int where, Player player)
        {
            var spot = Object.FindObjectsByType<LootSpot>(FindObjectsSortMode.None)
                .Where(s => s != null && s.IsHidden && Mathf.Abs(s.transform.position.y - at.y) < 1.5f
                            && new Vector2(s.transform.position.x - at.x, s.transform.position.z - at.z).magnitude < reach)
                .OrderBy(s => Vector3.Distance(s.transform.position, at)).FirstOrDefault();
            if (spot != null) Treasure.Take(spot, player, Prefix(where));
            else player.Message(MessageHud.MessageType.Center, Prefix(where) + Plugin.U5.Ovl.CStr(Treasure.SNothingOfNote).Trim());
        }

        /// <summary>The furniture a hidden spot lies in, by the original's words for it (0 none): a barrel or a stump.</summary>
        public static int FurnitureAt(Vector3 at)
        {
            bool Near(Component c) => c != null && new Vector2(c.transform.position.x - at.x, c.transform.position.z - at.z).magnitude < TileReach && Mathf.Abs(c.transform.position.y - at.y) < 1.5f;
            if (Object.FindObjectsByType<Fermenter>(FindObjectsSortMode.None).Any(Near)) return SInTheBarrel;
            if (SearchableStump.All.Any(Near)) return SInTheStump;
            return TownFurniture.At(at);
        }

        public static string SearchHover(string name) => Localization.instance.Localize(name + "\n[<color=yellow><b>$KEY_Use</b></color>] Search");
    }

    /// <summary>A tree stump in a settlement (tile 0x2b, a shrunken swamp tree; look "stump"): searched with E ("In the stump thou dost find …").</summary>
    public class SearchableStump : MonoBehaviour, Hoverable, Interactable
    {
        public static readonly System.Collections.Generic.List<SearchableStump> All = new System.Collections.Generic.List<SearchableStump>();
        void Awake()
        {
            All.Add(this);
            // the tree's own label ("Ancient Tree") sits on its trunk and would answer first
            foreach (var label in GetComponentsInChildren<HoverText>(true)) Destroy(label);
        }
        void OnDestroy() => All.Remove(this);

        public string GetHoverText() => Searching.SearchHover("Stump");
        public string GetHoverName() => "Stump";
        public float GetHoverOffset() => 0.5f;
        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !(user is Player p) || p != Player.m_localPlayer) return false;
            Searching.Search(transform.position, Searching.TileReach, Searching.SInTheStump, p);
            return true;
        }
    }
}
