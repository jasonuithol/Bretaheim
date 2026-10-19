# Changelog

## Unreleased (Phase 2: Britain, the castle and Iolo's hut; not yet published)
Settlements (built from TOWNE/DWELLING/CASTLE/KEEP.DAT, setting `BuildSettlements`; tried in-game on
Britain, Lord British's castle and Iolo's hut):
- 2 m × 2 m tiles, 4 m storeys. Walls are solid brick courses (or a cobblestone frame for cottages),
  wood floors upstairs, hip roofs of thatch, shingle or grausten over buildings only (bounded rooms
  entered by a door, archway or ladder), none under parapets except where the map draws roof tiles.
- Upper floors hold only what their ladders and stairs reach.
- Doors are 3 m tall; double doors are two frameless leaves that open from the middle.
- Lit wall torches, braziers (raised clear of the floor) and lantern poles that never burn out;
  white marble drinking fountains; candles on candelabra tables; tables of any length laid end to end.
- Shop signs: each guild icon is read from the player's TILES.16 and printed on a banner hung from
  its signpost (or on the shop wall indoors).
- Smithies: bellows facing the forge brazier, anvils, quenching buckets standing in the troughs the
  map draws as river water indoors.
- Armourers show what they sell on the wall behind the counter (Iolo's Bows: a wall of bows),
  staggered so long items don't overlap.
- Britain's plaza water is a flower bed of bushes, flowers and thistles.
- West, North and East Britanny are built too, moved apart from the castle so each has room for
  its slopes. North Britanny's stream runs in a ditch into its pond.
- Drawbridges: bridge planks with water at both ends are wooden decks over the river that swing
  up at night and down in the morning.
- Basements: Lord British's castle (and Blackthorn's palace, Yew, Serpent's Hold) have their cellars
  dug a full storey down under the stone ground floor, reached by the ladders in the corner towers.
- Townsfolk: the people of the built towns live their day from the original schedules, dressed
  by their kind (guards in iron with spears, mages in robes, villagers and merchants in Hildir's
  clothes); hover over them for their name.
- Town alarm: a villager who sees a monster runs for a guard, the rest hide at home behind locked
  doors; guards fetch more guards for big monsters, and reinforcements come if the fight drags on.
  The Britannys run to Lord British's castle for its guards.
- Ultima's numbers for every fight: creatures and townsfolk fight on their type's stats, the
  Avatar on Ultima hit points (30 a level), weapon attack values and armour defence; kills earn
  experience and Lord British's apparition raises the level when you wake. Tab shows the Ultima
  stats. On the Britannia surface the original's random encounters replace Valheim's monsters
  (`u5 encounter` with devcommands brings one); any other Valheim monster there is the Ultima
  creature of its body. No stamina in Britannia. The townsfolk's beds cannot be claimed. Hit points
  and damage numbers are shown as Ultima counts them (30 at level 1).
- Townsfolk are real creatures: monsters hunt them, guards fight with their weapons (and throw
  their spears), the fallen are back the next morning; players cannot hurt them.
- New Avatars arrive in Britannia with 150 gold.
- Shops: shopkeepers keep their hours, greet as in the original and sell their original stock at
  the original prices in Valheim's trade window; armourers buy your arms; healers heal and cure. Guards run down and kill
  monsters that come into town.
- Talk to townsfolk (E): their original conversations, typed keywords and Name/Job/Bye, with
  their words over their heads, the talk reading down from the top with your answers after the
  colon. They walk at their own pace, sit on chairs, sleep in beds and keep to one tile each.
- Ladders look like ladders (rails and rungs) and, as in Ultima, you walk through them; use any
  part to climb.
- Beds put their headboards against the nearest wall; footlockers face away from the bed.
- Shop fronts up to three tiles wide count as doorways, so those shops get their roofs.
- Window arches are full arches; tables of any length; the `SpawnLocation` setting starts you
  in Iolo's hut (default) or just outside another built settlement.
- Randotron: dead-end hallways may get a banner or a mounted animal head.
- Iolo's hut is the spawn point, with its tool chest, workbench and barley plot.
- Building shells can be merged into one object per zone (`CombinedShells`) to cut piece counts.

Overworld:
- Calmer mountains; gentle planted river banks with water lilies; soft, irregular biome borders.
- Sand beaches 0–20 m wide on Plains, Meadows and Ashlands coasts, with dunes and a seabed deep
  enough for waves; bushes, rocks and trees along coasts, rivers and paths.
- Roads are rustic 4 m dirt tracks that wander a little, level the ground across and clear their way.
- Overland signposts are glowing rune stones in a clearing.

Tools: `u5 dump`/U5Dump `town <loc> floors|probe|json`, `preview`, `slopes`, `dressing`, `banners`.

## 0.2.0
- Phase 1: new worlds are Britannia. A world is Britannia when its seed starts with "Britannia";
  new worlds get such a seed (setting `NewWorldsAreBritannia`), other seeds stay vanilla.
- Terrain and biomes from BRIT.DAT at 32 m per tile (`TileMetres`): grasslands → Plains,
  forest → Black Forest, brush → Meadows, swamp, mountains, peaks → Mistlands, desert → Ashlands,
  carved rivers, crisp coasts.
- Vanilla places (altars, crypts, traders) are not generated; you start at Iolo's hut.
- A sign and a map pin at each of the 56 places (towns, castles, keeps, dungeons, shrines, moongates).
- World-wrap prototype: sailing off an edge brings you back on the other side (untested in-game).
- Requires Jötunn 2.30.2.

## 0.1.0
- Phase 0 (foundation): BepInEx/Jötunn plugin skeleton with the `UltimaVDirectory` and `TileMetres` settings.
- Readers for the player's own Ultima V files: LZW, DATA.OVL tables, BRIT.DAT/UNDER.DAT, settlement
  maps with floors, .NPC schedules, .TLK conversations, DUNGEON.DAT, .CBT combat maps, LOOK2.DAT,
  SIGNS.DAT and INIT.GAM.
- `u5 dump <table>` console command (F5) prints any of those tables in-game.
- Mapping tables (terrain, monsters, items, NPC looks, status effects) in `mappings/`.
- No gameplay changes yet: the world is still Valheim's.
