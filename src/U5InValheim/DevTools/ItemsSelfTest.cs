using System;
using System.Collections;
using System.Linq;
using U5InValheim.Data;
using U5InValheim.WorldGen;
using U5InValheim.WorldGen.Core;
using UnityEngine;

namespace U5InValheim.DevTools
{
    /// <summary>
    /// U5_SELFTEST=items on a dedicated server: the worn items' status effects, the regalia, the
    /// night sky of the day, the sextant and the watch, a magic carpet laid by Britain, and Yew's
    /// locked cell door jimmied by a stand-in player with keys (then a sealed door, if one is
    /// loaded, opened with a skull key). Logged as "items self-test".
    /// </summary>
    public static class ItemsSelfTest
    {
        static void Log(string s) => Plugin.Log.LogInfo("items self-test " + s);

        public static IEnumerator Run(Func<Vector3, IEnumerator> generate)
        {
            var map = UltimaItems.Map;
            // what wearing each ring or amulet gives (its own status effect, not the trinket's)
            foreach (int id in Enumerable.Range(42, 6))
            {
                var def = map.Inventory(id);
                var s = ObjectDB.instance.GetItemPrefab(def.Prefab)?.GetComponent<ItemDrop>()?.m_itemData.m_shared;
                Log($"worn: {def.Name} ({def.Prefab}) type {s?.m_itemType}, equip effect {(s?.m_equipStatusEffect != null ? s.m_equipStatusEffect.name + " \"" + s.m_equipStatusEffect.m_name + "\"" : "none")}, adrenaline {s?.m_maxAdrenaline}, defence {Plugin.U5.Ovl.ItemDefense(id)}: {s?.m_description}");
            }
            foreach (var name in new[] { "Crown of Lord British", "Amulet of Lord British", "Sceptre of Lord British", "Black Badge", "Sextant", "Pocket Watch", "Spyglass", "Grapple", "Gem", "Magic Carpet", "Key", "Skull Key" })
            {
                var def = map.Special(name);
                var s = ObjectDB.instance.GetItemPrefab(def.Prefab)?.GetComponent<ItemDrop>()?.m_itemData.m_shared;
                Log($"item: {name} ({def.Prefab} from {def.Base}) type {s?.m_itemType}, stack {s?.m_maxStackSize}, equip effect {s?.m_equipStatusEffect?.name ?? "none"}: {s?.m_description}");
            }
            foreach (var se in new[] { "U5_SE_CrownofLordBritish", "U5_SE_AmuletofLordBritish" })
                Log($"party effect {se}: {(ObjectDB.instance.GetStatusEffect(se.GetStableHashCode()) != null ? "registered" : "MISSING")}");

            // the sky, the sextant, the watch
            var (year, month, day) = UseItems.Date();
            Log($"night sky of {day}-{month}-{year}: {string.Join("; ", ItemUse.NightSky(Plugin.U5.Ovl, year, month, day, UseItems.ShadowlordTowns()))}");
            var britain = Settlements.Get(2);
            var outside = britain.Origin + new Vector3(britain.Site.Half + 60f, 0, 0);
            var (tx, ty) = UseItems.Tile(outside);
            var (hour, minute) = UseItems.Clock();
            Log($"sextant east of Britain, tile ({tx},{ty}), outdoors {!UseItems.InSettlement(outside)}, at {hour}:{minute:00}: \"{ItemUse.Sextant(Plugin.U5.Ovl, !UseItems.InSettlement(outside), hour, tx, ty)}\"; at midnight \"{ItemUse.Sextant(Plugin.U5.Ovl, true, 0, tx, ty)}\"; in Britain \"{ItemUse.Sextant(Plugin.U5.Ovl, !UseItems.InSettlement(britain.Origin), 0, tx, ty)}\"");
            Log($"watch: \"{ItemUse.Watch(Plugin.U5.Ovl, hour, minute)}\"");

            // a magic carpet laid out in the land
            yield return generate(outside);
            var carpetPrefab = ZNetScene.instance.GetPrefab(MagicCarpet.PrefabName);
            if (carpetPrefab == null) Log("carpet: NO PREFAB");
            else
            {
                float ground = ZoneSystem.instance.GetGroundHeight(outside);
                var carpet = UnityEngine.Object.Instantiate(carpetPrefab, new Vector3(outside.x, ground + 0.5f, outside.z), Quaternion.identity);
                yield return new WaitForSeconds(1f);
                Log($"carpet: laid {carpet.transform.position.y - ground:0.00} m over the ground, ZNetView {carpet.GetComponent<ZNetView>()?.GetZDO() != null}, synced {carpet.GetComponent<ZSyncTransform>() != null}, piece {carpet.GetComponent<Piece>() != null}, wear {carpet.GetComponent<WearNTear>() != null}, colliders {carpet.GetComponentsInChildren<Collider>().Length}, hover \"{carpet.GetComponent<MagicCarpet>().GetHoverText().Replace("\n", " | ")}\"");
                ZNetScene.instance.Destroy(carpet);
            }

            // Yew's cell door, jimmied by a stand-in with keys
            var yew = Settlements.Get(Crime.JailLocation);
            if (yew == null) { Log("jimmy: Yew not built"); yield break; }
            yield return generate(yew.Origin);
            var (px, py, pz) = yew.Plan.TilePos(0, 6 * SettlementPlan.Size + 25);
            var doorAt = yew.Origin + new Vector3(px, py, pz);
            var door = UnityEngine.Object.FindObjectsByType<Door>(FindObjectsSortMode.None).OrderBy(d => Vector3.Distance(d.transform.position, doorAt)).FirstOrDefault();
            var body = UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab("Player"), doorAt + Vector3.back * 1.5f + Vector3.up * 0.3f, Quaternion.identity).GetComponent<Player>();
            body.enabled = false;
            var zdo = door?.GetComponent<ZNetView>()?.GetZDO();
            if (zdo == null) Log("jimmy: no cell door");
            else
            {
                var keyItem = ObjectDB.instance.GetItemPrefab(map.Special("Key").Prefab);
                body.GetInventory().AddItem(keyItem, 30);
                string lockWas = zdo.GetString(Settlements.LockKey);
                int tries = 0;
                while (zdo.GetString(Settlements.LockKey).Length > 0 && tries < 30) { TownDoors.TryUnlock(door, body, zdo.GetString(Settlements.LockKey)); tries++; }
                int left = body.GetInventory().GetAllItems().Where(i => i.m_dropPrefab?.name == keyItem.name).Sum(i => i.m_stack);
                Log($"jimmy: cell door lock \"{lockWas}\" now \"{zdo.GetString(Settlements.LockKey)}\" after {tries} tries, {30 - left} keys broke (dexterity {AvatarStats.Mine.Dexterity}: {ItemUse.JimmyChance(AvatarStats.Mine.Dexterity):P0} a try)");
                zdo.Set(Settlements.LockKey, lockWas);
            }
            // a magically sealed door (a sealed one loaded, else the cell door sealed for the test), and a skull key
            var sealedDoor = UnityEngine.Object.FindObjectsByType<Door>(FindObjectsSortMode.None).FirstOrDefault(d => d.GetComponent<ZNetView>()?.GetZDO()?.GetString(Settlements.LockKey) == "magic");
            string restore = null;
            if (sealedDoor == null && zdo != null) { sealedDoor = door; restore = zdo.GetString(Settlements.LockKey); zdo.Set(Settlements.LockKey, "magic"); }
            if (sealedDoor == null) Log("skull key: no sealed door loaded");
            else
            {
                var sz = sealedDoor.GetComponent<ZNetView>().GetZDO();
                bool without = TownDoors.TryUnlock(sealedDoor, body, "magic");
                body.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab(map.Special("Skull Key").Prefab), 2);
                bool with = TownDoors.TryUnlock(sealedDoor, body, "magic");
                Log($"skull key: sealed door at {sealedDoor.transform.position}: without a skull key {without}, with one {with}, lock now \"{sz.GetString(Settlements.LockKey)}\", skull keys left {body.GetInventory().GetAllItems().Where(i => i.m_dropPrefab?.name == map.Special("Skull Key").Prefab).Sum(i => i.m_stack)}");
                sz.Set(Settlements.LockKey, restore ?? "magic");
            }
            ZNetScene.instance.Destroy(body.gameObject);
        }
    }
}
