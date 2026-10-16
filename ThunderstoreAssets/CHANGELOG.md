# Changelog

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
